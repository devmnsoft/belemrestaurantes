using System.Net.Http.Json;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Infrastructure.Ai;

public sealed class TavilyOptions
{
    public bool Enabled { get; set; }
    public bool FreeTierConfirmed { get; set; }
    public string? ApiKey { get; set; }
    public int DailyRequestLimit { get; set; } = 30;
    public int MonthlyCreditLimit { get; set; } = 900;
}

/// <summary>Basic search only: one credit per attempt. No advanced search, extraction or crawling.</summary>
public sealed class TavilyWebSearch(HttpClient client, TavilyOptions options, IFreeTierQuota quota) : IWebSearch
{
    public async Task<WebSearchResult> SearchAsync(string query, CancellationToken ct)
    {
        if (!options.Enabled || !options.FreeTierConfirmed || string.IsNullOrWhiteSpace(options.ApiKey))
            return new("not_configured", []);
        if (string.IsNullOrWhiteSpace(query)) return new("empty", []);
        if (!await quota.TryReserveAsync("tavily_basic", Math.Clamp(options.DailyRequestLimit, 0, 900),
            Math.Clamp(options.MonthlyCreditLimit, 0, 900), ct)) return new("quota_exhausted", []);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
            request.Headers.Authorization = new("Bearer", options.ApiKey);
            request.Content = JsonContent.Create(new {
                query = query[..Math.Min(query.Length, 500)], search_depth = "basic", max_results = 8,
                auto_parameters = false, include_answer = false, include_raw_content = false, include_images = false
            });
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return new(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? "quota_exhausted" : "unavailable", []);
            // Bound the untrusted response before parsing it.
            await response.Content.LoadIntoBufferAsync(262144, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return new("unavailable", []);
            var sources = new List<WebSearchSource>();
            foreach (var item in results.EnumerateArray().Take(8))
            {
                var url = Read(item, "url", 2000);
                if (!IsPublicSourceUrl(url)) continue;
                var title=Read(item,"title",200);var snippet=Read(item,"content",1200);
                if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(snippet))continue;
                sources.Add(new(title,url,snippet,DateTimeOffset.UtcNow){Publisher=new Uri(url).Host});
            }
            return new(sources.Count == 0 ? "empty" : "ok", sources);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("unavailable", []); }
        catch (HttpRequestException) { return new("unavailable", []); }
        catch (JsonException) { return new("unavailable", []); }
    }

    public static bool IsPublicSourceUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        // These URLs are displayed, never fetched by this adapter.
        return uri.HostNameType == UriHostNameType.Dns && uri.Host.Contains('.') &&
            !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(JsonElement item, string name, int max) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text[..Math.Min(text.Length, max)] : "";
}
