using System.Net;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Infrastructure.Ai;

public sealed class BraveSearchOptions
{
    public bool Enabled {get;set;}
    public bool FreeTierConfirmed {get;set;}
    public string? ApiKey {get;set;}
    public int DailyRequestLimit {get;set;}=20;
    public int MonthlyRequestLimit {get;set;}=200;
}
public sealed class BraveWebSearch(HttpClient http,BraveSearchOptions options,IFreeTierQuota quota):IWebSearch
{
    public async Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)
    {
        if(!options.Enabled||!options.FreeTierConfirmed||string.IsNullOrWhiteSpace(options.ApiKey))return new("not_configured",[]){Provider="brave"};
        if(!await quota.TryReserveAsync("brave_search",Math.Clamp(options.DailyRequestLimit,0,100),Math.Clamp(options.MonthlyRequestLimit,0,1000),ct))return new("quota_exhausted",[]){Provider="brave"};
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.search.brave.com/res/v1/web/search?country=BR&search_lang=pt-br&count=8&q="+Uri.EscapeDataString(query[..Math.Min(query.Length,500)]));
            request.Headers.Add("X-Subscription-Token",options.ApiKey);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)return new(response.StatusCode==HttpStatusCode.TooManyRequests?"quota_exhausted":response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden?"credential_error":"unavailable",[]){Provider="brave",FailureCode="http_"+(int)response.StatusCode};
            await response.Content.LoadIntoBufferAsync(262144,deadline.Token);
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            if(!doc.RootElement.TryGetProperty("web",out var web)||!web.TryGetProperty("results",out var results)||results.ValueKind!=JsonValueKind.Array)return new("empty",[]){Provider="brave"};
            var sources=new List<WebSearchSource>();
            foreach(var row in results.EnumerateArray().Take(8))
            {
                string Read(string key)=>row.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
                var title=WebUtility.HtmlDecode(Read("title"));var url=Read("url");var snippet=WebUtility.HtmlDecode(Read("description"));
                if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(snippet)||!TavilyWebSearch.IsPublicSourceUrl(url))continue;
                sources.Add(new(title[..Math.Min(title.Length,200)],url,snippet[..Math.Min(snippet.Length,600)],DateTimeOffset.UtcNow){Publisher=new Uri(url).Host});
            }
            return new(sources.Count>0?"ok":"empty",sources.DistinctBy(s=>s.Url).ToArray()){Provider="brave"};
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)when(ex is OperationCanceledException or HttpRequestException or JsonException){return new("unavailable",[]){Provider="brave",FailureCode=ex is OperationCanceledException?"timeout":ex is HttpRequestException h?h.HttpRequestError.ToString():"invalid_payload"};}
    }
}
