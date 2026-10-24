using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Ai;

public sealed class PublicSearchCache
{
    private readonly ConcurrentDictionary<string,(DateTimeOffset Expires,WebSearchResult Result)> items=new();
    public bool TryGet(string key,out WebSearchResult? result)
    {
        if(items.TryGetValue(key,out var item)&&item.Expires>DateTimeOffset.UtcNow){result=item.Result;return true;}
        items.TryRemove(key,out _);result=null;return false;
    }
    public void Set(string key,WebSearchResult result,TimeSpan ttl)
    {
        if(items.Count>=128){foreach(var item in items.Where(i=>i.Value.Expires<=DateTimeOffset.UtcNow))items.TryRemove(item.Key,out _);if(items.Count>=128)items.TryRemove(items.OrderBy(i=>i.Value.Expires).First().Key,out _);}
        items[key]=(DateTimeOffset.UtcNow+ttl,result);
    }
}

public sealed class PublicSourcesOptions
{
    public bool Enabled { get; set; }
    public int DailyRequestLimit { get; set; } = 30;
    public int MonthlyRequestLimit { get; set; } = 300;
    public string QuotaService { get; set; } = "wikipedia_search";
}

/// <summary>Fixed Wikimedia API only. This is limited public-knowledge search, not a general web engine.</summary>
public sealed class PublicKnowledgeWebSearch(HttpClient client, PublicSourcesOptions options, IFreeTierQuota quota, PublicSearchCache cache, ILogger<PublicKnowledgeWebSearch>? logger=null) : IWebSearch
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static DateTimeOffset nextAllowed;
    public static string BuildQuery(string input)
    {
        var words=Regex.Matches(input.ToLowerInvariant(),@"[\p{L}\p{N}-]+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value);
        string[] stop=["me","diga","quais","qual","pratos","prato","restaurante","restaurantes","onde","comer","como","conhecer","o","a","os","as","um","uma","de","do","da","dos","das","em","no","na","nos","nas","e","que","fazer","quero","saber","sobre","passeio","roteiro","posso","tem","cardápio","cardapio","atual","é","há","hoje","preços","preco","preço","horários","horario","consulte","pesquise","era","seria","significa","explique","fale"];
        var terms=words.Where(w=>!stop.Contains(w)).Distinct().Take(8).ToArray();
        return string.Join(" ",terms.Length==0?["Belém","Pará"]:terms);
    }
    public async Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)
    {
        if(!options.Enabled)return new("not_configured",[]);
        query=BuildQuery(query[..Math.Min(query.Length,500)]);
        var key="public-search:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
        if(cache.TryGet(key,out var cached))return cached! with{Status=cached!.Sources.Count>0?"public_sources_cached":cached.Status};
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(7));
        var locked=false;
        try
        {
            await Gate.WaitAsync(timeout.Token);locked=true;
            if(cache.TryGet(key,out cached))return cached! with{Status=cached!.Sources.Count>0?"public_sources_cached":cached.Status};
            var delay=nextAllowed-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,timeout.Token);
            if(!await quota.TryReserveAsync(options.QuotaService,Math.Clamp(options.DailyRequestLimit,0,100),Math.Clamp(options.MonthlyRequestLimit,0,1000),timeout.Token))return new("quota_exhausted",[]);
            nextAllowed=DateTimeOffset.UtcNow.AddSeconds(1);
            // No user-controlled host or path and no following redirects: no arbitrary-page fetch or SSRF surface.
            var url="https://pt.wikipedia.org/w/api.php?action=query&format=json&utf8=1&list=search&srnamespace=0&srlimit=3&srprop=snippet&maxlag=5&srsearch="+Uri.EscapeDataString(query);
            using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)return Remember(new(response.StatusCode==HttpStatusCode.TooManyRequests?"quota_exhausted":"unavailable",[]),TimeSpan.FromSeconds(30));
            await response.Content.LoadIntoBufferAsync(131072,timeout.Token);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if(!json.RootElement.TryGetProperty("query",out var data)||!data.TryGetProperty("search",out var items)||items.ValueKind!=JsonValueKind.Array)return Remember(new("unavailable",[]),TimeSpan.FromSeconds(30));
            var sources=new List<WebSearchSource>();
            foreach(var item in items.EnumerateArray().Take(3))
            {
                if(!item.TryGetProperty("pageid",out var id)||!id.TryGetInt64(out var pageId)||pageId<=0)continue;
                if(!item.TryGetProperty("title",out var titleValue)||titleValue.ValueKind!=JsonValueKind.String)continue;
                var title=titleValue.GetString()??"";
                var snippet=item.TryGetProperty("snippet",out var s)?s.GetString()??"":"";
                snippet=WebUtility.HtmlDecode(Regex.Replace(snippet,"<[^>]*>","",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)));
                if(title.Length is <2 or >200||snippet.Length==0)continue;
                var core=BuildQuery(query).Split(' ').Where(w=>w.Length>=4&&!new[]{"ilha","belém","pará","belem","para"}.Contains(w)).ToArray();
                var text=BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(title+" "+snippet).ToLowerInvariant();
                if(core.Length>0&&!core.Any(w=>Regex.IsMatch(text,@"\b"+Regex.Escape(BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(w))+@"\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))))continue;
                sources.Add(new(title+" · Wikipédia (CC BY-SA)","https://pt.wikipedia.org/?curid="+pageId,snippet[..Math.Min(snippet.Length,500)],DateTimeOffset.UtcNow){Publisher="Wikipédia",SourceKind="encyclopedia"});
            }
            return Remember(new(sources.Count>0?"public_sources":"empty",sources),TimeSpan.FromMinutes(30));
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {logger?.LogWarning("Pesquisa pública falhou: {FailureType}; HTTP {HttpError}.",ex.GetType().Name,ex is HttpRequestException h?h.HttpRequestError.ToString():"deadline_or_payload");return Remember(new("unavailable",[]){Provider="wikipedia",FailureCode=ex is HttpRequestException h2?h2.HttpRequestError.ToString():ex is OperationCanceledException?"timeout":"invalid_payload"},TimeSpan.FromSeconds(30));}
        finally{if(locked)Gate.Release();}
        WebSearchResult Remember(WebSearchResult result,TimeSpan ttl){cache.Set(key,result,ttl);return result;}
    }
}

public sealed class CompositeWebSearch(TavilyWebSearch general,PublicKnowledgeWebSearch publicSources,BraveWebSearch? brave=null,PublicSearchCache? cache=null,DraelWebSearch? drael=null,PublicSiteSearch? regional=null):IWebSearch
{
    private static readonly SemaphoreSlim Gate=new(1);
    public async Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)
    {
        var key="composite:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
        if(cache is not null&&cache.TryGet(key,out var cached))return cached! with{Status=cached!.Status=="ok"?"cached":cached.Status=="public_sources"?"public_sources_cached":cached.Status};
        if(!await Gate.WaitAsync(TimeSpan.FromSeconds(3),ct))return new("unavailable",[]){FailureCode="search_busy"};
        try
        {
            if(cache is not null&&cache.TryGet(key,out cached))return cached! with{Status=cached!.Status=="ok"?"cached":cached.Status=="public_sources"?"public_sources_cached":cached.Status};
            var requests=new List<Task<WebSearchResult>>{general.SearchAsync(query,ct)};
            if(!BelemRestaurantes.Application.Services.CulinaryQuery.Operational(query))requests.Add(publicSources.SearchAsync(query,ct));
            if(brave is not null)requests.Add(brave.SearchAsync(query,ct));
            if(drael is not null)requests.Add(drael.SearchAsync(query,ct));
            if(regional is not null)requests.Add(regional.SearchAsync(query,ct));
            var results=await Task.WhenAll(requests);
            var sources=BelemRestaurantes.Application.Services.SearchRanking.Merge(query,results.SelectMany(r=>r.Sources)).ToArray();
            var failure=results.FirstOrDefault(r=>r.Status!="not_configured"&&r.Status!="empty");
            var result=new WebSearchResult(sources.Length>0?(sources.All(s=>s.SourceKind=="encyclopedia")?"public_sources":"ok"):failure?.Status??"empty",sources){Provider=string.Join("+",results.Select(r=>r.Provider).Where(p=>p is not null).Distinct()),FailureCode=sources.Length==0?failure?.FailureCode:null};
            cache?.Set(key,result,TimeSpan.FromSeconds(result.Sources.Count>0?1200:result.Status=="empty"?120:20));
            return result; // Each adapter independently enforces its configured quotas; no limit is increased.
        }
        finally{Gate.Release();}
    }
}
