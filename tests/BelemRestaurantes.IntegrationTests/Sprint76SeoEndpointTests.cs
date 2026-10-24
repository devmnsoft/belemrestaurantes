using System.Net;
using System.Xml.Linq;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Controllers;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 76 — o portal ganhou descoberta por buscadores: /robots.txt e /sitemap.xml.
// Contrato provado em ponta: todo conteúdo publicado está listado, nenhum pendente aparece,
// toda URL estática do sitemap responde 200 e o XML obedece ao namespace oficial.
[Collection("MySql")]
public sealed class Sprint76SeoEndpointTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString()!,
                ["Seo:PublicOrigin"] = "https://localhost",
                ["Discovery:Enabled"] = "false",
                ["Crawler:Enabled"] = "false"
            })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<(int Estabelecimentos, int Receitas, int Roteiros, int Cidades, int Localidades, int Categorias)> DirectCountsAsync()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        using var scope = root.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        return await db.QueryFirstAsync<(int, int, int, int, int, int)>(new CommandDefinition("""
            SELECT
              (SELECT COUNT(*) FROM restaurants WHERE status='published'),
              (SELECT COUNT(*) FROM para_recipes WHERE status='published'),
              (SELECT COUNT(*) FROM gastronomic_trails WHERE is_active=TRUE AND (editorial_json IS NULL OR editorial_state='published')),
              (SELECT COUNT(DISTINCT REPLACE(normalized_name,' ','-')) FROM geo_municipalities WHERE normalized_name<>''),
              (SELECT COUNT(DISTINCT REPLACE(normalized_name,' ','-')) FROM geo_districts_localities WHERE is_tourist_highlight=TRUE AND normalized_name<>''),
              (SELECT COUNT(DISTINCT category) FROM restaurants WHERE status='published' AND category IS NOT NULL AND TRIM(category)<>'')
            """));
    }

    [Fact]
    public async Task RobotsTxtDeclaresSitemapAndProtectsPrivateRegions()
    {
        using var app = Factory();
        using var client = CreateClient(app);
        var response = await client.GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("User-agent: *", text);
        Assert.Contains("Disallow: /admin", text);
        Assert.Contains("Disallow: /api", text);
        Assert.Contains("Disallow: /account", text);
        Assert.Contains("Sitemap: https://localhost/sitemap.xml", text);
    }

    [Fact]
    public async Task SitemapListsEveryPublishedItemAndInventsNothing()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var marker = "sprint76-" + Guid.NewGuid().ToString("N");
        long pendingId = 0, publishedId = 0;
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client = CreateClient(app);
        try
        {
            // Um pendente (fora do público) e um aprovado (dentro do público) — o sitemap não pode confundir os dois.
            pendingId = await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>()
                .UpsertDiscoveredAsync(new DiscoveredPlace(marker + "-pendente", "Casa de homologação pendente", null, "Belém", "Restaurante", "Rua de homologação", null, null, "https://example.org/fixture", null, null, null, []),
                    "test_fixture", DateTimeOffset.UtcNow, default);
            publishedId = await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>()
                .UpsertDiscoveredAsync(new DiscoveredPlace(marker + "-publicado", "Casa de homologação publicada", null, "Belém", "Restaurante", "Rua de homologação", null, null, "https://example.org/fixture", null, null, null, []),
                    "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(publishedId, "fixture-admin", default));

            var response = await client.GetAsync("/sitemap.xml");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
            var xml = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xml); // quebra aqui se o sitemap não for XML bem formado
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            Assert.Equal("urlset", doc.Root!.Name.LocalName);
            var urls = doc.Root.Elements(ns + "url").ToList();
            var locs = urls.Select(u => u.Element(ns + "loc")!.Value).ToList();

            // Nenhuma URL duplicada e todas absolutas na origem configurada.
            Assert.Equal(locs.Count, locs.Distinct().Count());
            Assert.All(locs, loc => Assert.StartsWith("https://localhost/", loc));

            // Contagem reconciliada com SQL direto: o sitemap é espelho exato do que está publicado.
            var (est, rec, rot, cid, loc, cat) = await DirectCountsAsync();
            var expected = SeoController.StaticPaths.Count + ParaTasteCatalog.Dishes.Count + ParaTasteCatalog.Occasions.Count
                           + est + rec + rot + cid + loc + cat;
            var xEst = locs.Count(l => l.Contains("/estabelecimento/", StringComparison.Ordinal));
            var xRec = locs.Count(l => l.StartsWith("https://localhost/receitas/", StringComparison.Ordinal));
            var xRot = locs.Count(l => l.StartsWith("https://localhost/roteiros/", StringComparison.Ordinal));
            var xCid = locs.Count(l => l.StartsWith("https://localhost/cidade/", StringComparison.Ordinal));
            var xLoc = locs.Count(l => l.StartsWith("https://localhost/localidade/", StringComparison.Ordinal));
            var xCat = locs.Count(l => l.StartsWith("https://localhost/categoria/", StringComparison.Ordinal));
            Assert.True(urls.Count == expected,
                $"sitemap={urls.Count} esperado={expected} | xml est={xEst} rec={xRec} rot={xRot} cid={xCid} loc={xLoc} cat={xCat} staticFlavor={locs.Count - xEst - xRec - xRot - xCid - xLoc - xCat} | db est={est} rec={rec} rot={rot} cid={cid} loc={loc} cat={cat} | publicado={publishedId}");

            // O aprovado entra com lastmod; o pendente nem aparece.
            var publishedEntry = urls.First(u => u.Element(ns + "loc")!.Value == "https://localhost/estabelecimento/" + publishedId);
            Assert.NotNull(publishedEntry.Element(ns + "lastmod"));
            Assert.DoesNotContain("/estabelecimento/" + pendingId, xml);

            // Amostras de cada seção dinâmica presentes quando o banco tem conteúdo (e contadas acima).
            Assert.Contains("https://localhost/pratos/tacaca", locs);
            if (cid > 0) Assert.Contains(locs, l => l.StartsWith("https://localhost/cidade/"));
            if (loc > 0) Assert.Contains(locs, l => l.StartsWith("https://localhost/localidade/"));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject='fixture-admin' AND entity_type='restaurant' AND entity_id=@id", new { id = publishedId });
            await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM restaurants WHERE id=@id", new { id = publishedId });
            await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM restaurants WHERE id=@id", new { id = pendingId });
        }
    }

    [Fact]
    public async Task EveryStaticPathInTheSitemapResponds200()
    {
        using var app = Factory();
        using var client = CreateClient(app);
        foreach (var path in SeoController.StaticPaths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, "rota estática do sitemap sem resposta pública: " + path);
        }
    }
}
