using System.Net;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
namespace BelemRestaurantes.UnitTests;
public sealed class Sprint48AiTests
{
    [Fact]
    public async Task GeminiOmitsThoughtPartsJoinsAnswerPartsAndAccountsForThinkingTokens()
    {
        const string body="""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"thought":true,"text":"PRIVATE THINKING"},{"text":"{\"answer\":"},{"text":"\"Resposta\"}"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":20,"thoughtsTokenCount":7}}""";
        var provider=new GeminiProvider(new HttpClient(new Handler(body)),new(){ApiKey="fixture-only",BaseUrl="https://example.org/v1beta",Model="fixture"});
        var result=await provider.CompleteAsync(new("Contexto","Pergunta","test"),default);
        Assert.True(result.IsValid);Assert.Equal("{\"answer\":\"Resposta\"}",result.RawContent);Assert.DoesNotContain("PRIVATE",result.RawContent);Assert.Equal(27,result.OutputTokens);
    }
    [Fact]
    public void ProviderExceptionsNeverRetainAnUntrustedErrorBody()
    {
        var error=new AiProviderException(AiProvider.Gemini,403,"untrusted-secret-value");
        Assert.DoesNotContain("untrusted-secret-value",error.ToString());Assert.Equal(403,error.StatusCode);
    }
    [Fact]
    public async Task WorkerFreeCallsUsePurposeAndSharedAccountCapsAndKeepMonetaryCostZero()
    {
        var provider=new TestProvider();var quota=new Quota();var ledger=new Ledger();
        var router=new AiRouter([provider],ledger,new Options(),NullLogger<AiRouter>.Instance,quota,new(){ConfirmedFreeProviders=["Gemini"],DailyRequestLimit=20,MonthlyRequestLimit=200,WorkerDailyRequestLimit=2,WorkerMonthlyRequestLimit=20});
        Assert.NotNull(await router.ClassifyAsync("test","{}",default));Assert.NotNull(await router.ClassifyAsync("test","{}",default));
        Assert.Null(await router.ClassifyAsync("test","{}",default));Assert.Equal(2,provider.Calls);Assert.Equal(2,quota.Counts["cuia_gemini"]);Assert.All(ledger.Costs,cost=>Assert.Equal(0,cost));
        var exhausted=new Quota();exhausted.Counts["cuia_gemini"]=20;
        var blocked=new TestProvider();var other=new AiRouter([blocked],ledger,new Options(),NullLogger<AiRouter>.Instance,exhausted,new(){ConfirmedFreeProviders=["Gemini"],DailyRequestLimit=20});
        Assert.Null(await other.ClassifyAsync("test","{}",default));Assert.Equal(0,blocked.Calls);
    }
    private sealed class Options:IOptionsSnapshotLike{public IReadOnlyList<AiProvider> ProviderChain=>[AiProvider.Gemini];}
    private sealed class TestProvider:IAiProvider
    {
        public int Calls;public AiProvider Provider=>AiProvider.Gemini;public bool IsConfigured=>true;public TokenPrice Price=>new(100,400);
        public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request,CancellationToken ct){Calls++;return Task.FromResult(new AiCompletionResponse(Provider,"{\"categoria\":\"Restaurante\",\"cidade\":\"Belém\",\"confianca\":0.9}",100,50,1000,TimeSpan.Zero,true));}
    }
    private sealed class Quota:IFreeTierQuota
    {
        public Dictionary<string,int> Counts=[];
        public Task<bool> TryReserveAsync(string service,int daily,int monthly,CancellationToken ct){var count=Counts.GetValueOrDefault(service);if(count>=Math.Min(daily,monthly))return Task.FromResult(false);Counts[service]=count+1;return Task.FromResult(true);}
    }
    private sealed class Ledger:ICostLedger
    {
        public List<long> Costs=[];public Task<bool> CanUseFreeTierAsync(string provider,CancellationToken ct)=>Task.FromResult(true);
        public Task RecordAsync(AiCostEntry entry,CancellationToken ct){Costs.Add(entry.CostMicrosUsd);return Task.CompletedTask;}
        public Task<decimal> MonthToDateUsdAsync(string p,DateTimeOffset n,CancellationToken ct)=>Task.FromResult(0m);
        public Task<IReadOnlyList<(string Provider,decimal CostUsd)>> BreakdownAsync(DateTimeOffset now,CancellationToken ct)=>Task.FromResult<IReadOnlyList<(string,decimal)>>([]);
        public Task<bool> HasBudgetRemaining(string p,decimal spend,CancellationToken ct)=>Task.FromResult(false);
        public Task<IReadOnlyList<AiCostActivity>> RecentAsync(int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<AiCostActivity>>([]);
    }
    private sealed class Handler(string body):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});}
}
