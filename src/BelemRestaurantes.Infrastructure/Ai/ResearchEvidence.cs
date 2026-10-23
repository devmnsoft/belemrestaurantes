using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Ai;

public sealed class ResearchEvidenceOptions{public bool FetchPages{get;set;}=true;}
public sealed class ResearchEvidenceCache
{
    private readonly ConcurrentDictionary<string,(DateTimeOffset Until,WebSearchSource Source)> entries=new();
    public bool TryGet(string key,out WebSearchSource? source){source=null;if(!entries.TryGetValue(key,out var e)||e.Until<DateTimeOffset.UtcNow)return false;source=e.Source;return true;}
    public void Set(string key,WebSearchSource source){if(entries.Count>=128)foreach(var k in entries.OrderBy(x=>x.Value.Until).Take(16).Select(x=>x.Key))entries.TryRemove(k,out _);entries[key]=(DateTimeOffset.UtcNow.AddMinutes(source.RetrievalStatus=="retrieved"?20:1),source);}
}

// Fetch only public results through the existing DNS-pinned client and robots policy.
public sealed class ResearchEvidence(ISafeHttpClient http,IRobotsTxtService robots,ICrawlerParser parser,
    ResearchEvidenceCache cache,ILogger<ResearchEvidence> logger,ResearchEvidenceOptions? options=null):IResearchEvidence
{
    public async Task<WebSearchResult> EnrichAsync(WebSearchResult search,string city,CancellationToken ct)
    {
        if(options is {FetchPages:false})return search;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var sources=search.Sources.Where(s=>!string.IsNullOrWhiteSpace(s.Title)&&TavilyWebSearch.IsPublicSourceUrl(s.Url)).DistinctBy(s=>s.Url).Take(8).ToArray();
        for(var i=0;i<Math.Min(3,sources.Length);i++)
        {
            var source=sources[i];
            if(source.SourceKind is "reviewed_reference" or "encyclopedia")continue;
            if(cache.TryGet(source.Url,out var cached)){sources[i]=cached!;continue;}
            var host=new Uri(source.Url).Host;
            if(IsSocial(host)){sources[i]=source with{Publisher=host,SourceKind=source.SourceKind=="ai_discovery"?"ai_social_link":"social_index",RetrievalStatus="indexed_only"};continue;}
            try
            {
                var access=await robots.CheckAccessAsync(source.Url,"CuiaCrawler",deadline.Token);
                if(!access.IsAllowed||access.CrawlDelaySeconds>5){sources[i]=source with{RetrievalStatus="restricted",Publisher=host};cache.Set(source.Url,sources[i]);continue;}
                if(access.CrawlDelaySeconds>0)await Task.Delay(TimeSpan.FromSeconds(access.CrawlDelaySeconds),deadline.Token);
                var result=await http.DownloadAsync(source.Url,timeoutSeconds:5,ct:deadline.Token);
                if(!result.Success||result.ContentText is null||result.ContentType?.Contains("html")!=true){sources[i]=source with{RetrievalStatus="unavailable",Publisher=host};cache.Set(source.Url,sources[i]);continue;}
                var html=result.ContentText;
                if(source.SourceKind=="ai_discovery")
                {
                    var titleMatch=Regex.Match(html,@"<title[^>]*>(.*?)</title>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100));
                    var pageTitle=titleMatch.Success?BelemRestaurantes.Application.Services.ExternalContentRules.Clean(titleMatch.Groups[1].Value,200):"";
                    if(pageTitle.Length<3){sources[i]=source with{RetrievalStatus="unavailable"};continue;}
                    source=source with{Title=pageTitle};
                }
                var places=parser.ExtractRestaurants(html,source.Url,city).Where(p=>p.ConfidenceScore>=.95&&!p.HasConflict&&NameNormalizer.Normalize(source.Title+" "+source.Snippet).Contains(NameNormalizer.Normalize(p.Name))).ToArray();
                var place=places.Length==1?places[0]:null;
                var declaredSameSite=place?.DeclaredWebsite==true && place.ExplicitCity && place.Website is {} website&&Uri.TryCreate(website,UriKind.Absolute,out var official)&&SameHost(host,official.Host);
                // Same-domain structured metadata is a source assertion, not proof of ownership or today's menu.
                sources[i]=source with{Publisher=host,SourceKind=place is not null&&declaredSameSite?"structured_site":"editorial_page",RetrievalStatus="retrieved",PublishedAt=PublicationDate(html),PlaceName=place?.Name,City=place?.City,Address=place?.Address,MenuItems=declaredSameSite?place!.MenuItems:[],OpeningHours=declaredSameSite?place!.OpeningHours:[]};
                cache.Set(source.Url,sources[i]);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)when(ex is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException)
            {sources[i]=source with{RetrievalStatus="timeout",Publisher=host};logger.LogWarning("Pesquisa: recuperação da fonte {Host} terminou com {Type}.",host,ex.GetType().Name);}
            if(deadline.IsCancellationRequested)break;
        }
        return search with{Sources=sources};
    }
    public static bool SameHost(string a,string b)=>string.Equals(a.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?a[4..]:a,b.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?b[4..]:b,StringComparison.OrdinalIgnoreCase);
    public static bool IsSocial(string host)=>new[]{"instagram.com","tiktok.com","linkedin.com","facebook.com"}.Any(h=>host==h||host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    private static DateTimeOffset? PublicationDate(string html)
    {
        var m=Regex.Match(html,"""<meta[^>]+(?:property|name)=["']article:published_time["'][^>]+content=["']([^"']+)["']""",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
        return m.Success&&DateTimeOffset.TryParse(WebUtility.HtmlDecode(m.Groups[1].Value),out var date)?date:null;
    }
}
