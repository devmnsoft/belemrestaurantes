using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BelemRestaurantes.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class DiscoveryOperationsTests
{
    [Fact]
    public async Task RegistryPageCheckpointIsDurableIdempotentAndScopedToPageContent()
    {
        using var provider=(ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
        var db=provider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>();
        var hash=BelemRestaurantes.Infrastructure.OpenData.RegistryImportLedger.HashPage(Guid.NewGuid().ToString());
        var ledger=new BelemRestaurantes.Infrastructure.OpenData.RegistryImportLedger(db);
        try
        {
            Assert.False(await ledger.IsCompletedAsync(hash,default));
            await ledger.CompleteAsync(hash,1024,1,default);
            await ledger.CompleteAsync(hash,1024,1,default);
            Assert.True(await new BelemRestaurantes.Infrastructure.OpenData.RegistryImportLedger(db).IsCompletedAsync(hash,default));
            Assert.False(await ledger.IsCompletedAsync(BelemRestaurantes.Infrastructure.OpenData.RegistryImportLedger.HashPage(hash),default));
            await Assert.ThrowsAsync<ArgumentException>(()=>ledger.CompleteAsync(hash,1025,1,default));
        }
        finally
        {
            await using var connection=await db.OpenAsync(default);
            await Dapper.SqlMapper.ExecuteAsync(connection,"DELETE FROM registry_import_pages WHERE page_sha256=@hash",new{hash});
        }
    }

    [Fact]
    public async Task CoverageShowsOfficialMunicipalitiesAndCombinesRegionWithAccentInsensitiveSearch()
    {
        using var factory=CreateFactory();
        using var scope=factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/admin/cobertura"));
        Assert.Equal(144,System.Text.RegularExpressions.Regex.Matches(html,"<th scope=\"row\">").Count);
        html=WebUtility.HtmlDecode(await client.GetStringAsync("/admin/cobertura?region="+Uri.EscapeDataString("Baixo Amazonas")+"&q=santarem"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html,"<th scope=\"row\">"));
        Assert.Contains("<th scope=\"row\">Santarém",html);
        html=await client.GetStringAsync("/admin/cobertura?region="+Uri.EscapeDataString("Marajó")+"&q=santarem");
        Assert.Empty(System.Text.RegularExpressions.Regex.Matches(html,"<th scope=\"row\">"));
    }

    [Fact]
    public async Task ManualImportTelemetryDoesNotRequireOrConsumeScheduledJob()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
        var telemetry=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.ICycleTelemetryRepository>();
        await using var connection=await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default);
        var id=await telemetry.StartCycleAsync("fixture-manual-import",DateTimeOffset.UtcNow,default);
        try
        {
            var work=new BelemRestaurantes.Application.Models.DiscoveryWorkItem(0,0,"Belém","regional",1);
            var metrics=BelemRestaurantes.Application.Models.DiscoveryCycleMetrics.Empty with { Source="openstreetmap" };
            await telemetry.RecordTargetAsync(id,work,metrics,default);
            Assert.Equal(1,await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM discovery_target_runs WHERE cycle_id=@id AND job_id IS NULL AND source='openstreetmap'",new{id}));
        }
        finally
        {
            await Dapper.SqlMapper.ExecuteAsync(connection,"DELETE FROM discovery_target_runs WHERE cycle_id=@id",new{id});
            await Dapper.SqlMapper.ExecuteAsync(connection,"DELETE FROM discovery_cycle_runs WHERE id=@id",new{id});
        }
    }

    [Fact]
    public async Task ReviewedCorrectionUpdatesOriginal_AndNewSuggestionRemainsPendingPublication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IRestaurantIngestionRepository>();
        var moderation = scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IModerationRepository>();
        var suggestions = scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.ISuggestionRepository>();
        await using var connection = await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default);
        var marker = Guid.NewGuid().ToString("N");
        var restaurantIds = new List<long>(); var suggestionIds = new List<long>();
        try
        {
            var place = new BelemRestaurantes.Application.Models.DiscoveredPlace("fixture-review-" + marker, "Original " + marker, null, "Belém", "Restaurante", "Rua de homologação", null, null, "https://example.org/fixture", null, null, null, ["restaurant"]);
            var original = await ingestion.UpsertDiscoveredAsync(place,"test_fixture",DateTimeOffset.UtcNow,default);
            restaurantIds.Add(original);
            Assert.True(await moderation.ApproveAsync(original,"fixture-review",default));
            var correction = await suggestions.AddAsync("Corrigido " + marker,"Belém",null,"Revisão controlada.",default,original);
            suggestionIds.Add(correction);
            var html = await client.GetStringAsync("/admin/suggestions");
            var token = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.False(string.IsNullOrWhiteSpace(token));
            var response = await client.PostAsync($"/admin/suggestions/{correction}/accept",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
            Assert.Equal("Corrigido " + marker, await Dapper.SqlMapper.ExecuteScalarAsync<string>(connection,"SELECT name FROM restaurants WHERE id=@original",new{original}));
            Assert.Equal(0,await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection,"SELECT COUNT(*) FROM restaurants WHERE google_place_id=@external",new{external="suggestion:"+correction}));
            var fresh = await suggestions.AddAsync("Novo " + marker,"Belém",null,null,default);
            suggestionIds.Add(fresh);
            response = await client.PostAsync($"/admin/suggestions/{fresh}/accept",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
            var pendingId = await Dapper.SqlMapper.ExecuteScalarAsync<long>(connection,"SELECT id FROM restaurants WHERE google_place_id=@external",new{external="suggestion:"+fresh});
            restaurantIds.Add(pendingId);
            Assert.Equal("pending",await Dapper.SqlMapper.ExecuteScalarAsync<string>(connection,"SELECT status FROM restaurants WHERE id=@pendingId",new{pendingId}));
        }
        finally
        {
            if(suggestionIds.Count>0) await Dapper.SqlMapper.ExecuteAsync(connection,"DELETE FROM suggestions WHERE id IN @suggestionIds",new{suggestionIds});
            if(restaurantIds.Count>0) await Dapper.SqlMapper.ExecuteAsync(connection,"DELETE FROM restaurants WHERE id IN @restaurantIds",new{restaurantIds});
        }
    }

    [Theory]
    [InlineData("/admin/discovery", "Descoberta por cidade e fonte")]
    [InlineData("/admin/cobertura", "Meta de expansão: 100 mil estabelecimentos reais")]
    [InlineData("/admin/crawler", "Cuia Crawler")]
    [InlineData("/admin/discovery360", "Discovery")]
    [InlineData("/admin/suggestions", "Boas dicas")]
    [InlineData("/admin/referencias", "Referências")]
    [InlineData("/account", "Perfil")]
    [InlineData("/account/favorites", "favoritos")]
    [InlineData("/partner/dashboard", "Parceiro")]
    public async Task Operator_views_render_real_database_metrics(string route, string expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, html);
        Assert.Contains(expected, WebUtility.HtmlDecode(html));
        // Snapshot somente do HTML autorizado por este host de teste. Nenhuma
        // credencial real ou exceção de autenticação é adicionada à aplicação.
        var output = Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, route.Replace('/', '-').TrimStart('-') + ".html"), html);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development")
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() }))
                .ConfigureServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "test-operator";
                        options.DefaultChallengeScheme = "test-operator";
                    }).AddScheme<AuthenticationSchemeOptions, OperatorHandler>("test-operator", _ => { });
                    services.Configure<AuthenticationOptions>(o=>o.SchemeMap[BelemRestaurantes.Application.Abstractions.AdminAuthDefaults.Scheme].HandlerType=typeof(OperatorHandler));
                    services.PostConfigure<AuthenticationOptions>(options =>
                        options.Schemes.First(s => s.Name == BelemRestaurantes.Application.Abstractions.MemberAuth.Scheme).HandlerType = typeof(OperatorHandler));
                }));

    private sealed class OperatorHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "test-operator"), new Claim(ClaimTypes.Name, "Operador de teste"),
                new Claim(ClaimTypes.Role, AdminRoles.PlatformAdmin), new Claim("amr", "mfa")
                , new Claim("sid", "fixture-session"), new Claim("email_verified", "true")
            }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
