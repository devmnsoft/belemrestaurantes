using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Sprint 69 — frescor dos espelhos Overpass persistido para o painel: round-trip do store e
/// exposição em /admin/geocodificacao com os vereditos conservadores (antigo/fresco/ilegível).</summary>
[Collection("MySql")]
public sealed class Sprint69MirrorFreshnessTests
{
    private const string EspelhoA = "https://espelho-s69-a.local/api/interpreter";
    private const string EspelhoB = "https://espelho-s69-b.local/api/interpreter";
    // Espelhos do conjunto padrão configurado na Web (mesmos que o Worker usa localmente).
    private const string Kumi = "https://overpass.kumi.systems/api/interpreter";
    private const string OsmCh = "https://overpass.osm.ch/api/interpreter";

    private sealed class StoredRow
    {
        public string Endpoint { get; set; } = "";
        public DateTime? Base { get; set; }
        public DateTime Observed { get; set; }
        public StoredRow() { }
    }

    private static async Task<StoredRow?> ReadRowAsync(IDbConnection db, string endpoint) =>
        await db.QueryFirstOrDefaultAsync<StoredRow>(new CommandDefinition(
            "SELECT endpoint AS Endpoint, base_timestamp AS Base, observed_at AS Observed FROM overpass_mirror_freshness WHERE endpoint=@ep", new { ep = endpoint }));

    private static async Task UpsertOrDeleteAsync(IDbConnection db, string endpoint, StoredRow? previous)
    {
        if (previous is null)
        {
            await db.ExecuteAsync(new CommandDefinition("DELETE FROM overpass_mirror_freshness WHERE endpoint=@ep", new { ep = endpoint }));
            return;
        }
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE overpass_mirror_freshness SET base_timestamp=@baseTs, observed_at=@observed WHERE endpoint=@ep
            """, new { ep = endpoint, baseTs = previous.Base, observed = previous.Observed }));
    }

    private static async Task<(HttpClient Client, string UserId)> LoginAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var verifier = scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpProvider>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.Totp.v1");
        var id = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid() + "!Safe";
        var secret = totp.GenerateSecret();
        var record = new AdminUserRecord(id, "test-" + id, id + "@example.invalid", verifier.Hash(password), true,
            protector.Protect(Encoding.UTF8.GetBytes(secret)), 0, null, null);
        await users.CreateAsync(record, [AdminRoles.Moderator], default);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var login = await PostForm(client, "/admin/login", "/admin/login", new() { ["userName"] = record.UserName!, ["password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var code = totp.GenerateFor(DateTimeOffset.UtcNow, secret);
        var verify = await PostForm(client, "/admin/mfa/verify", "/admin/mfa/verify", new() { ["code"] = code, ["returnUrl"] = "/admin/geocodificacao" });
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        return (client, id);
    }

    private static async Task DeleteUserAsync(ServiceProvider root, string userId)
    {
        await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        await db.ExecuteAsync("DELETE FROM admin_user_roles WHERE user_id=@id;DELETE FROM admin_users WHERE id=@id", new { id = userId });
    }

    [Fact]
    public async Task StoreRoundTripsUpsertAndUnreadableAge()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        try
        {
            using var scope = root.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOverpassMirrorFreshnessStore>();
            var now = DateTimeOffset.UtcNow;

            await store.RecordAsync(EspelhoA, now.AddDays(-30), now.AddMinutes(-2), default);
            await store.RecordAsync(EspelhoA, now.AddDays(-5), now.AddMinutes(-1), default); // upsert pela chave
            await store.RecordAsync(EspelhoB, null, now.AddMinutes(1), default);             // idade ilegível -> NULL

            var latest = await store.GetLatestAsync(default);
            var a = latest.Single(r => r.Endpoint == EspelhoA);
            Assert.NotNull(a.BaseTimestampUtc);
            var b = latest.Single(r => r.Endpoint == EspelhoB);
            Assert.Null(b.BaseTimestampUtc);

            // Diferença entre duas leituras da mesma conexão não depende da interpretação de fuso
            // (o deslocamento da sessão é uniforme e se cancela); o valor bruto no servidor é o que
            // foi escrito como wall-clock UTC.
            Assert.True(Math.Abs((a.ObservedAtUtc - b.ObservedAtUtc).TotalMinutes + 2) <= 0.1, "observed_at: upsert guardou -1min vs +1min");
            await using var dbCheck = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            var rawBase = await dbCheck.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                "SELECT base_timestamp FROM overpass_mirror_freshness WHERE endpoint=@ep", new { ep = EspelhoA }));
            // Wall-clock gravado é o UTC escrito (o driver grava a parede como está); 1 µs de folga pela precisão DATETIME(6).
            Assert.True(Math.Abs((rawBase - now.AddDays(-5).UtcDateTime).Ticks) <= 10, $"raw={rawBase:o} esperado={now.AddDays(-5).UtcDateTime:o}");

            var idade = now - a.BaseTimestampUtc.Value;
            Assert.InRange(idade, TimeSpan.FromDays(4) + TimeSpan.FromHours(18), TimeSpan.FromDays(5) + TimeSpan.FromHours(3)); // base de ~5 dias, ± interpretação de sessão
            Assert.True(a.IsDataOld(now, 4));  // teto estrito de 4 dias: 5 dias já é antigo
            Assert.False(a.IsDataOld(now));    // teto padrão de 14 dias: fresco
            Assert.True(b.IsDataOld(now));     // ilegível -> conservadoramente antigo
        }
        finally
        {
            await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            await UpsertOrDeleteAsync(db, EspelhoA, null);
            await UpsertOrDeleteAsync(db, EspelhoB, null);
        }
    }

    [Fact]
    public async Task PanelShowsWorkerMirrorObservationsWithConservativeVerdicts()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        string userId = "";
        var previousKumi = (StoredRow?)null;
        var previousOsmCh = (StoredRow?)null;
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var db0 = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            previousKumi = await ReadRowAsync(db0, Kumi);
            previousOsmCh = await ReadRowAsync(db0, OsmCh);

            using (var scope = root.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOverpassMirrorFreshnessStore>();
                await store.RecordAsync(Kumi, now.AddDays(-30), now.AddMinutes(-5), default);   // dado antigo
                await store.RecordAsync(OsmCh, now.AddDays(-2), now.AddHours(-1), default);     // fresco
            }

            (var client, userId) = await LoginAsync(factory);
            using (client)
            {
                var page = await client.GetStringAsync("/admin/geocodificacao");
                Assert.Contains("Espelhos Overpass (frescor dos dados)", page);
                Assert.Contains("overpass.kumi.systems", page);
                Assert.Contains("overpass.osm.ch", page);
                Assert.Contains("dado antigo (acima de 14 dias)", page);
                Assert.Contains("fresco (menos de 14 dias)", page);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                // A data-base exibida segue a convenção wall-clock uniforme do repositório; em máquina
                // fora do UTC o driver pode devolver a leitura com deslocamento de sessão — os dois candidatos.
                var dataEsperada1 = now.AddDays(-30).ToString("dd/MM/yyyy", inv);
                var dataEsperada2 = now.AddDays(-30).AddHours(3).ToString("dd/MM/yyyy", inv);
                Assert.True(page.Contains(dataEsperada1) || page.Contains(dataEsperada2), "data-base do kumi no painel");
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            await UpsertOrDeleteAsync(db, Kumi, previousKumi);
            await UpsertOrDeleteAsync(db, OsmCh, previousOsmCh);
        }
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string page, string action, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        fields["__RequestVerificationToken"] = Token(html);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields));
    }
}
