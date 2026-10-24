using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 77 — compartilhamento de verdade: cada ficha pública expõe og:url absoluto, og:image
// absoluto quando tem foto e JSON-LD schema.org/Restaurant com a regra de honestidade do portal
// (a chave só existe quando o dado existe na ficha). Provar em ponta contra o banco local.
[Collection("MySql")]
public sealed class Sprint77SharingTests
{
    private const string Origin = "https://localhost";

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString()!,
                ["Seo:PublicOrigin"] = Origin,
                ["Discovery:Enabled"] = "false",
                ["Crawler:Enabled"] = "false"
            })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Origin) });

    private static async Task<long> SeedPublishedAsync(IServiceScope scope, string marker)
    {
        var id = await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>()
            .UpsertDiscoveredAsync(new DiscoveredPlace(marker, "Sprint 77 casa publicada", null, "Belem", "Restaurante",
                "Rua de homologacao 77", null, null, "https://example.org/fixture", null, null, null, []),
                "test_fixture", DateTimeOffset.UtcNow, default);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(id, "fixture-admin", default));
        return id;
    }

    // Extrai o conteúdo do primeiro <script type="application/ld+json"> para as asserções
    // verificarem o schema em vez de caçar substrings no HTML inteiro. O Razor escreve o "+" da
    // type como &#x2B; (escapamento de atributo), então a busca para o início do bloco não
    // depende do sufixo literal.
    private static string LdJsonBlock(string html)
    {
        const string openStart = "<script type=\"application/ld";
        var start = html.IndexOf(openStart, StringComparison.Ordinal);
        Assert.True(start >= 0, "a ficha não publicou bloco ld+json");
        var contentStart = html.IndexOf('>', start) + 1;
        var end = html.IndexOf("</script>", contentStart, StringComparison.Ordinal);
        Assert.True(end > contentStart, "bloco ld+json sem fechamento");
        return html[contentStart..end];
    }

    [Fact]
    public async Task EstablishmentCardPublishesOgUrlAndHonestRestaurantSchema()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        long id = 0;
        using var client = CreateClient(app);
        try
        {
            id = await SeedPublishedAsync(scope, "sprint77-" + Guid.NewGuid().ToString("N"));
            // Baseline determinístico: nada de foto/telefone/website/coordenada nesta ficha.
            await db.ExecuteAsync("""UPDATE restaurants SET photo_url=NULL, latitude=NULL, longitude=NULL, phone=NULL, whatsapp=NULL, website=NULL WHERE id=@id""", new { id });

            var response = await client.GetAsync($"/estabelecimento/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var json = LdJsonBlock(html);

            Assert.Contains("<meta property=\"og:url\" content=\"" + Origin + "/estabelecimento/" + id + "\" />", html);
            Assert.Contains("\"@type\":\"Restaurant\"", json);
            Assert.Contains($"\"url\":\"{Origin}/estabelecimento/{id}\"", json);
            // Honestidade: sem foto/telefone/coordenada na ficha, as chaves nem existem no schema.
            Assert.DoesNotContain("property=\"og:image\"", html);
            Assert.DoesNotContain("\"image\"", json);
            Assert.DoesNotContain("\"telephone\"", json);
            Assert.DoesNotContain("\"sameAs\"", json);
            Assert.DoesNotContain("\"geo\"", json);
        }
        finally
        {
            if (id > 0) await CleanupAsync(db, id);
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task EstablishmentCardAddsKeysOnlyWhenDataExists()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        long id = 0;
        using var client = CreateClient(app);
        try
        {
            id = await SeedPublishedAsync(scope, "sprint77-" + Guid.NewGuid().ToString("N"));
            await db.ExecuteAsync("""UPDATE restaurants SET photo_url='/media/photo/sprint77.webp', phone='93 3222-4455', website='https://casa.example.org', latitude=-1.4550013, longitude=-48.4931780 WHERE id=@id""", new { id });

            var response = await client.GetAsync($"/estabelecimento/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var json = LdJsonBlock(html);

            Assert.Contains("<meta property=\"og:image\" content=\"" + Origin + "/media/photo/sprint77.webp\" />", html);
            Assert.Contains("\"telephone\":\"93 3222-4455\"", json);
            Assert.Contains("\"sameAs\":\"https://casa.example.org\"", json);
            Assert.Contains("\"geo\"", json);
            Assert.Contains("\"latitude\":\"-1.4550013\"", json);
            Assert.Contains("\"longitude\":\"-48.493178\"", json);
        }
        finally
        {
            if (id > 0) await CleanupAsync(db, id);
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task DishesAndTrailPagesExposeCanonicalOgUrl()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client = CreateClient(app);

        foreach (var entry in ParaTasteCatalog.Dishes.Take(1).Concat(ParaTasteCatalog.Occasions.Take(1)))
        {
            var section = entry.Kind == "prato" ? "pratos" : "ocasioes";
            var response = await client.GetAsync($"/{section}/{entry.Slug}");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{section}/{entry.Slug} respondeu {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains($"<meta property=\"og:url\" content=\"{Origin}/{section}/{entry.Slug}\" />", html);
        }

        var trailSlug = (string?)(await db.ExecuteScalarAsync(
            """SELECT slug FROM gastronomic_trails WHERE is_active=TRUE AND (editorial_json IS NULL OR editorial_state='published') AND (editorial_json IS NOT NULL OR EXISTS(SELECT 1 FROM trail_stops ts WHERE ts.trail_id=gastronomic_trails.id)) ORDER BY slug LIMIT 1"""));
        if (trailSlug is not null)
        {
            var response = await client.GetAsync($"/roteiros/{trailSlug}");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"roteiros/{trailSlug} respondeu {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains($"<meta property=\"og:url\" content=\"{Origin}/roteiros/{trailSlug}\" />", html);
        }
    }

    private static async Task CleanupAsync(System.Data.Common.DbConnection db, long id) =>
        await db.ExecuteAsync("""DELETE FROM audit_events WHERE actor_subject='fixture-admin' AND entity_type='restaurant' AND entity_id=@id;DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM restaurants WHERE id=@id""", new { id });
}
