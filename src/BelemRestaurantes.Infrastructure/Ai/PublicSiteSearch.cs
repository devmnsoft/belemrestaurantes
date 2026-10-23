using System.Text.Json;
using Microsoft.Extensions.Logging;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
namespace BelemRestaurantes.Infrastructure.Ai;
public sealed class PublicSiteSearchOptions{public bool Enabled{get;set;}public int DailyRequestLimit{get;set;}=30;public int MonthlyRequestLimit{get;set;}=300;}
public sealed class PublicSiteSearch(ISafeHttpClient http,IRobotsTxtService robots,IFreeTierQuota quota,PublicSiteSearchOptions options,ILogger<PublicSiteSearch>? logger=null):IWebSearch
{
    // Public search indexes verified at these official regional sources; no API key or paid plan.
    private static readonly string[] Endpoints=["https://marituba.pa.gov.br/site/wp-json/wp/v2/search","https://ideflorbio.pa.gov.br/wp-json/wp/v2/search"];
    public async Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)
    {
        if(!options.Enabled)return new("not_configured",[]){Provider="public_sites"};
        query=CulinaryQuery.Correct(query);
        var culinary=CulinaryQuery.IsCulinary(query);
        var city=SearchLanguage.Municipality(query);
        var cityWords=city is null?Array.Empty<string>():SearchTopics.Normalize(city).Split(' ');
        var terms=SearchLanguage.Terms(query).Where(t=>t is not ("restaurante" or "restaurantes" or "cardapio")&&!cityWords.Contains(t)).ToArray();
        if(terms.Length==0)terms=["gastronomia"];
        var words=System.Text.RegularExpressions.Regex.Matches(SearchTopics.Normalize(query),@"[\p{L}\p{N}]+",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value).ToArray();
        var start=Array.IndexOf(words,terms[0]);var end=Array.LastIndexOf(words,terms[^1]);
        var phrase=start>=0&&end>=start?string.Join(" ",words.Skip(start).Take(end-start+1)):string.Join(" ",terms);
        if(culinary){terms=CulinaryQuery.Core(query);phrase=string.Join(" ",terms);}
        if(terms.Length==0)return new("empty",[]){Provider="public_sites"};
        var endpoints=culinary?new[]{"https://combu.com.br/wp-json/wp/v2/search","https://www.restauranteremanso.com.br/wp-json/wp/v2/search"}:Endpoints;
        var calls=endpoints.Select(async endpoint=>
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(culinary?26:18));
            try
            {
                var url=endpoint+"?per_page=8&search="+Uri.EscapeDataString("\""+phrase+"\"");
                var access=await robots.CheckAccessAsync(url,"CuiaCrawler",deadline.Token);if(!access.IsAllowed||access.CrawlDelaySeconds>10)return Array.Empty<WebSearchSource>();
                if(access.CrawlDelaySeconds>0)await Task.Delay(TimeSpan.FromSeconds(access.CrawlDelaySeconds),deadline.Token);
                if(!await quota.TryReserveAsync("public_site_search",Math.Clamp(options.DailyRequestLimit,0,100),Math.Clamp(options.MonthlyRequestLimit,0,1000),deadline.Token))return [];
                var response=await http.DownloadAsync(url,timeoutSeconds:5,ct:deadline.Token);if(!response.Success||response.ContentText is null||response.ContentText.Length>262144){logger?.LogWarning("Índice público {Host}: HTTP {Status} sem resultado utilizável.",new Uri(endpoint).Host,response.StatusCode);return [];}
                using var doc=JsonDocument.Parse(response.ContentText);if(doc.RootElement.ValueKind!=JsonValueKind.Array)return [];
                var rows=new List<WebSearchSource>();
                foreach(var row in doc.RootElement.EnumerateArray().Take(8))
                {
                    if(!row.TryGetProperty("title",out var title)||title.ValueKind!=JsonValueKind.String||!row.TryGetProperty("url",out var link)||link.ValueKind!=JsonValueKind.String)continue;
                    var original=ExternalContentRules.CanonicalUrl(link.GetString()??"");if(original is null||!ResearchEvidence.SameHost(new Uri(original).Host,new Uri(endpoint).Host))continue;
                    var text=ExternalContentRules.Clean(title.GetString()??"",200);if(text.Length<3)continue;
                    var normalized=" "+BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(text)+" ";
                    if(!terms.All(t=>normalized.Contains(" "+t+" ",StringComparison.Ordinal)))
                    {
                        if(!culinary||rows.Count>0)continue;
                        var pageAccess=await robots.CheckAccessAsync(original,"CuiaCrawler",deadline.Token);
                        if(!pageAccess.IsAllowed||pageAccess.CrawlDelaySeconds>10)continue;
                        if(pageAccess.CrawlDelaySeconds>0)await Task.Delay(TimeSpan.FromSeconds(pageAccess.CrawlDelaySeconds),deadline.Token);
                        if(!await quota.TryReserveAsync("public_site_search",Math.Clamp(options.DailyRequestLimit,0,100),Math.Clamp(options.MonthlyRequestLimit,0,1000),deadline.Token))continue;
                        var page=await http.DownloadAsync(original,timeoutSeconds:5,ct:deadline.Token);
                        if(!page.Success||page.ContentText is null)continue;
                        var visible=ExternalContentRules.Clean(page.ContentText,100000);
                        if(!CulinaryQuery.Relevant(query,text,visible))continue;
                        rows.Add(new(text,original,"A página pública contém menções a "+string.Join(", ",terms)+". Consulte o conteúdo original; uma menção não comprova receita completa ou venda atual.",DateTimeOffset.UtcNow){Publisher=new Uri(original).Host,SourceKind="public_index",RetrievalStatus="retrieved"});
                        continue;
                    }
                    rows.Add(new(text,original,"Referência encontrada no índice público da fonte. Abra a matéria original para consultar os detalhes e a data.",DateTimeOffset.UtcNow){Publisher=new Uri(original).Host,SourceKind="public_index",RetrievalStatus="indexed_only"});
                }
                return rows.ToArray();
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)when(ex is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException){logger?.LogWarning("Índice público {Host}: {FailureType}.",new Uri(endpoint).Host,ex.GetType().Name);return Array.Empty<WebSearchSource>();}
        });
        var sources=SearchRanking.Merge(query,(await Task.WhenAll(calls)).SelectMany(x=>x));return new(sources.Count>0?"ok":"empty",sources){Provider="public_sites"};
    }
}
