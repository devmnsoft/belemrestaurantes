using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Enums;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint47JourneyTests
{
    [Fact]
    public async Task ExhaustedSessionBlocksCloudAndCookieSurvivesConversationReset()
    {
        using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        app.Services.GetRequiredService<FixtureQuota>().AllowUnits=false;
        using var first=await client.GetAsync("/cuia-ia?q=Quero+conhecer+o+açaí");
        first.EnsureSuccessStatusCode();var html=WebUtility.HtmlDecode(await first.Content.ReadAsStringAsync());
        Assert.Contains("O limite de IA desta sessão foi atingido",html);
        var cookie=first.Headers.GetValues("Set-Cookie").Single(c=>c.StartsWith("cuia-usage-session="));
        Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0,app.Services.GetRequiredService<FixtureProvider>().Calls);
        using var next=await client.GetAsync("/cuia-ia?q=Quero+conhecer+o+cupuaçu");next.EnsureSuccessStatusCode();
        Assert.False(next.Headers.TryGetValues("Set-Cookie",out var cookies)&&cookies.Any(c=>c.StartsWith("cuia-usage-session=")));
        Assert.Equal(0,app.Services.GetRequiredService<FixtureProvider>().Calls);
    }

    [Fact]
    public async Task ExternalDiscoveryRendersWithSourcesButNeverPublishesAPlace()
    {
        using var app = Factory(); using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        var html = await client.GetStringAsync("/cuia-ia?q=Quero+um+restaurante+fora+do+catálogo+em+Belém&searchWeb=true");
        var folder=Environment.GetEnvironmentVariable("SPRINT47_SNAPSHOTS");if(folder is not null){Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"cuia-external.html"),html);}
        Assert.Contains("Casa Fonte Sprint47", html); Assert.Contains("Descobertas além do catálogo", WebUtility.HtmlDecode(html));
        Assert.Contains("https://example.org/casa-fonte", html); Assert.Contains("Indicar este lugar ao portal", html);
        Assert.Contains("data-save-kind=\"external\"", html);
        using var scope=app.Services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurants WHERE name='Casa Fonte Sprint47'"));
    }

    [Fact]
    public async Task ScopeAndSessionContextWorkThroughTheActualEndpoint()
    {
        using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var html=await client.GetStringAsync("/cuia-ia");var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/cuia-ia/ask"){Content=JsonContent.Create(new{prompt="Escreva programação em Python para mim",previousQuestions=new[]{"Onde comer em Belém?"}})};
        request.Headers.Add("X-CSRF-TOKEN",token);using var response=await client.SendAsync(request);
        response.EnsureSuccessStatusCode();using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("limite",data.RootElement.GetProperty("answerKind").GetString());
        Assert.Equal(0,app.Services.GetRequiredService<FixtureProvider>().Calls);
        using var longRequest=new HttpRequestMessage(HttpMethod.Post,"/api/cuia-ia/ask"){Content=JsonContent.Create(new{prompt="Onde comer?",previousQuestions=new string('x',501) is {} longText?new[]{longText}:[]})};
        longRequest.Headers.Add("X-CSRF-TOKEN",token);using var rejected=await client.SendAsync(longRequest);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
    }

    [Theory]
    [InlineData("/descobrir")]
    [InlineData("/descobrir?moment=familia&city=Bel%C3%A9m&dish=tacaca")]
    [InlineData("/meu-passeio")]
    public async Task DiscoveryJourneysArePublicAndUsableWithoutLogin(string path)
    {
        using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();Assert.Contains("discovery.js",html);Assert.DoesNotContain("action=\"/account/login\"",html);
    }

    [Fact]
    public async Task ExternalIndicationIsPrefilledForReviewAndPendingDuplicatesAreAtomic()
    {
        using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var html=await client.GetStringAsync("/sugerir?name=Casa+Fonte+Sprint47&city=Bel%C3%A9m&source=https%3A%2F%2Fexample.org%2Fcasa-fonte");
        Assert.Contains("value=\"Casa Fonte Sprint47\"",html);Assert.Contains("Fonte externa para revis",html);
        Assert.DoesNotContain("name=\"Website\" value=\"https://example.org",html);
        using var scope=app.Services.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>();
        var unique="Dica Sprint47 "+Guid.NewGuid().ToString("N")[..12];
        try
        {
            var tasks=Enumerable.Range(0,8).Select(async _=>{using var child=app.Services.CreateScope();return await child.ServiceProvider.GetRequiredService<ISuggestionRepository>().AddAsync(unique,"Belém",null,"Fixture para revisão",default);});
            var ids=await Task.WhenAll(tasks);Assert.Single(ids.Distinct());
            await using var db=await factory.OpenAsync(default);Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM suggestions WHERE name=@unique AND status='pending'",new{unique}));
        }
        finally{await using var db=await factory.OpenAsync(default);await db.ExecuteAsync("DELETE FROM suggestions WHERE name=@unique",new{unique});}
    }

    private static WebApplicationFactory<Program> Factory()=>new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
        builder.UseEnvironment("Development").ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:MySql"]=DatabaseEnvironment.ConnectionString(),["Cuia:FreeTier:ConfirmedFreeProviders:0"]="Groq"
        }));
        builder.ConfigureTestServices(services=>{
            services.RemoveAll<CuiaFreeTierOptions>();services.AddSingleton(new CuiaFreeTierOptions{ConfirmedFreeProviders=["Groq"]});
            services.RemoveAll<IAiProvider>();services.AddSingleton<FixtureProvider>();services.AddSingleton<IAiProvider>(sp=>sp.GetRequiredService<FixtureProvider>());
            services.RemoveAll<IWebSearch>();services.AddSingleton<IWebSearch,FixtureSearch>();
            services.RemoveAll<IFreeTierQuota>();services.AddSingleton<FixtureQuota>();services.AddSingleton<IFreeTierQuota>(sp=>sp.GetRequiredService<FixtureQuota>());
            services.RemoveAll<ICostLedger>();services.AddSingleton<ICostLedger,FixtureLedger>();
        });
    });

    private sealed class FixtureSearch:IWebSearch
    {
        public Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)=>Task.FromResult(new WebSearchResult("ok",[
            new("Casa Fonte Sprint47 em Belém","https://example.org/casa-fonte","A Casa Fonte Sprint47, em Belém, apresenta cozinha regional. Confirme a operação com o responsável.",DateTimeOffset.UtcNow)]));
    }
    private sealed class FixtureQuota:IFreeTierQuota
    {public bool AllowUnits=true;public Task<bool> TryReserveAsync(string service,int dailyLimit,int monthlyLimit,CancellationToken ct)=>Task.FromResult(true);public Task<bool> TryReserveUnitsAsync(string service,int dailyLimit,int monthlyLimit,int units,CancellationToken ct)=>Task.FromResult(AllowUnits);}
    private sealed class FixtureProvider:IAiProvider
    {
        public int Calls;public AiProvider Provider=>AiProvider.Groq;public bool IsConfigured=>true;public TokenPrice Price=>new(0,0);
        public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request,CancellationToken ct)
        {
            Calls++;Assert.True(request.JsonOutput);Assert.Contains("dado não confiável",request.SystemInstruction);
            const string raw="""{"answer":"Encontrei uma opção externa para você explorar. Consulte a fonte antes de sair.","externalSuggestions":[{"name":"Casa Fonte Sprint47","city":"Belém","neighborhood":null,"evidence":"apresenta cozinha regional.","sourceNumbers":[1]}]}""";
            return Task.FromResult(new AiCompletionResponse(Provider,raw,100,80,0,TimeSpan.Zero,true));
        }
    }
    private sealed class FixtureLedger:ICostLedger
    {
        public Task<bool> CanUseFreeTierAsync(string provider,CancellationToken ct)=>Task.FromResult(true);
        public Task RecordAsync(AiCostEntry entry,CancellationToken ct)=>Task.CompletedTask;
        public Task<decimal> MonthToDateUsdAsync(string provider,DateTimeOffset now,CancellationToken ct)=>Task.FromResult(0m);
        public Task<IReadOnlyList<(string Provider,decimal CostUsd)>> BreakdownAsync(DateTimeOffset now,CancellationToken ct)=>Task.FromResult<IReadOnlyList<(string,decimal)>>([]);
        public Task<bool> HasBudgetRemaining(string provider,decimal alreadySpentUsd,CancellationToken ct)=>Task.FromResult(false);
        public Task<IReadOnlyList<AiCostActivity>> RecentAsync(int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<AiCostActivity>>([]);
    }
}
