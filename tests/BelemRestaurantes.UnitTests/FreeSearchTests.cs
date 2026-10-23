using System.Net;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Ai;

namespace BelemRestaurantes.UnitTests;

public sealed class FreeSearchTests
{
    [Fact]
    public async Task TavilyUsesOneCreditBasicSearchAndFiltersUnsafeSources()
    {
        var handler = new Handler("""{"results":[{"title":"Fonte","url":"https://example.org/fonte","content":"Indício"},{"url":"javascript:alert(1)"},{"url":"https://127.0.0.1/a"},{"url":"https://user:pass@example.org/a"}]}""");
        using var client = new HttpClient(handler);
        var quota = new Quota();
        var search = new TavilyWebSearch(client, Options(), quota);
        var result = await search.SearchAsync("Travessia do Combu", default);
        Assert.Equal("ok", result.Status);
        Assert.Single(result.Sources);
        Assert.Equal("Bearer fixture-only", handler.Authorization);
        Assert.Equal("https://api.tavily.com/search", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("basic", body.RootElement.GetProperty("search_depth").GetString());
        Assert.False(body.RootElement.GetProperty("auto_parameters").GetBoolean());
        Assert.False(body.RootElement.GetProperty("include_raw_content").GetBoolean());
        Assert.Equal(900, quota.MonthlyLimit);
        Assert.DoesNotContain("fixture-only", handler.Body);
    }

    [Theory]
    [InlineData(false, true, "not_configured")]
    [InlineData(true, false, "quota_exhausted")]
    public async Task MissingFreeAccountOrQuotaMakesNoHttpRequest(bool confirmed, bool quotaAllowed, string status)
    {
        var handler = new Handler("{}"); using var client = new HttpClient(handler);
        var options = Options(); options.FreeTierConfirmed = confirmed;
        var search = new TavilyWebSearch(client, options, new Quota(quotaAllowed));
        Assert.Equal(status, (await search.SearchAsync("Belém", default)).Status);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(429, "{}", "quota_exhausted")]
    [InlineData(503, "{}", "unavailable")]
    [InlineData(200, "not-json", "unavailable")]
    public async Task SearchErrorsAreExplicit(int code, string body, string expected)
    {
        using var client = new HttpClient(new Handler(body, (HttpStatusCode)code));
        var result = await new TavilyWebSearch(client, Options(), new Quota()).SearchAsync("Belém", default);
        Assert.Equal(expected, result.Status); Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderOutputContractPreservesJsonClassificationAndAllowsConciergeText(bool json)
    {
        foreach (var gemini in new[] { true, false })
        {
            var handler = new Handler(gemini ? """{"candidates":[{"content":{"parts":[{"text":"resposta"}]}}]}""" : """{"choices":[{"message":{"content":"resposta"}}]}""");
            using var client = new HttpClient(handler);
            var options = new AiProviderOptions { ApiKey = "fixture-only", BaseUrl = "https://example.org/", Model = "fixture" };
            IAiProvider provider = gemini ? new GeminiProvider(client, options) : new GroqProvider(client, options);
            await provider.CompleteAsync(new AiCompletionRequest("instruction", "payload", "test", JsonOutput: json), default);
            using var body = JsonDocument.Parse(handler.Body!);
            Assert.Equal(gemini ? (json ? "application/json" : "text/plain") : (json ? "json_object" : "text"),
                gemini ? body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString()
                    : body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        }
    }

    private static TavilyOptions Options() => new() { Enabled = true, FreeTierConfirmed = true, ApiKey = "fixture-only", MonthlyCreditLimit = 99999 };
    private sealed class Quota(bool allowed = true) : IFreeTierQuota
    {
        public int MonthlyLimit;
        public Task<bool> TryReserveAsync(string service, int dailyLimit, int monthlyLimit, CancellationToken ct)
        { MonthlyLimit = monthlyLimit; return Task.FromResult(allowed); }
    }
    private sealed class Handler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public string? Body, Authorization, Url;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Body = await request.Content!.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString(); Url = request.RequestUri!.ToString();
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
