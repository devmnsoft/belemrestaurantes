using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 79 — as páginas de catálogo do sitemap (/cidade/{slug}, /localidade/{slug},
// /categoria/{slug}) passam a carregar o próprio cartão de compartilhamento: og:url canônico na
// origem pública, og:title/description construídos apenas com o que o catálogo respondeu, e a
// paginação apontando sempre para a primeira página. A descrição é comparada contra a mesma função
// pura (CatalogPageSeo) alimentada pela contagem real lida do banco no momento do teste — nada
// codificado no assert.
[Collection("MySql")]
public sealed class Sprint79CatalogSharingTests
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

    private static async Task<int> CountAsync(WebApplicationFactory<Program> app, string sql)
    {
        using var scope = app.Services.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        return await db.ExecuteScalarAsync<int>(sql);
    }

    private static string OgAttribute(string html, string property)
    {
        var match = Regex.Match(html, $"<meta property=\"{property}\" content=\"([^\"]*)\"");
        Assert.True(match.Success, $"não achei <meta property=\"{property}\"> na página");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task CityPagesCarryTheirOwnCanonicalCard()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/cidade/belem");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("https://localhost/cidade/belem", OgAttribute(html, "og:url"));
        Assert.Equal("Restaurantes em Belém | Belém Restaurantes", OgAttribute(html, "og:title"));
        Assert.Equal("website", OgAttribute(html, "og:type"));

        // A descrição usa a contagem real publicada em Belém — mesma função pura do serviço.
        var count = await CountAsync(app, "SELECT COUNT(*) FROM restaurants WHERE status='published' AND normalized_city='belem'");
        Assert.Equal(CatalogPageSeo.Description(count, "city", "Belém"), OgAttribute(html, "og:description"));
    }

    [Fact]
    public async Task PaginationDoesNotChangeTheCanonicalUrl()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var first = await client.GetAsync("/cidade/belem");
        var second = await client.GetAsync("/cidade/belem?page=2");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstHtml = HttpUtility.HtmlDecode(await first.Content.ReadAsStringAsync());
        var secondHtml = HttpUtility.HtmlDecode(await second.Content.ReadAsStringAsync());
        Assert.Equal(OgAttribute(firstHtml, "og:url"), OgAttribute(secondHtml, "og:url"));
        Assert.DoesNotContain("page=2", OgAttribute(secondHtml, "og:url"));
    }

    [Fact]
    public async Task LocalityPagesPointToTheirOwnUrlAndRealCount()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        // Praia do Atalaia: zona turística publicada no sitemap (S78 devolveu-a a Salinópolis).
        var response = await client.GetAsync("/localidade/praia-do-atalaia");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("https://localhost/localidade/praia-do-atalaia", OgAttribute(html, "og:url"));
        Assert.Contains("Praia do Atalaia", OgAttribute(html, "og:title"));

        var count = await CountAsync(app, "SELECT COUNT(*) FROM restaurants WHERE status='published' AND neighborhood_normalized='praia do atalaia'");
        Assert.Equal(CatalogPageSeo.Description(count, "locality", "Praia do Atalaia"), OgAttribute(html, "og:description"));
    }

    [Fact]
    public async Task CategoryPagesShareTheExactPublishedCount()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client = CreateClient(app);

        // Fixture hermética (a CI roda contra instalação limpa): um restaurante publicado com um
        // rótulo de categoria próprio, na mesma grafia que a ingestão o guardaria (ToDisplayCase).
        const string label = "Loja de Alimentos S79";
        const string slug = "loja-de-alimentos-s79";
        var marker = Guid.NewGuid().ToString("N");
        var id = await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>()
            .UpsertDiscoveredAsync(new(marker, "Fixture " + marker, null, "Belém", label, "Rua da homologação", null, null, "https://example.org/fixture", null, null, null, []), "test_fixture", DateTimeOffset.UtcNow, default);
        try
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(id, "fixture-admin", default));

            var response = await client.GetAsync("/categoria/" + slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            // O rótulo exibido é exatamente o que o catálogo guardou, e a contagem é a do banco.
            Assert.Equal("https://localhost/categoria/" + slug, OgAttribute(html, "og:url"));
            Assert.Equal(label + " no Pará | Belém Restaurantes", OgAttribute(html, "og:title"));
            var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurants WHERE status='published' AND category=@c", new { c = label });
            Assert.True(count >= 1, "fixture não ficou publicada");
            Assert.Equal(CatalogPageSeo.Description(count, "category", label), OgAttribute(html, "og:description"));
        }
        finally
        {
            await db.ExecuteAsync("""
                DELETE FROM audit_events WHERE actor_subject IN ('test_fixture','fixture-admin') AND entity_type='restaurant' AND entity_id=@id;
                DELETE FROM source_observations WHERE restaurant_id=@id;
                DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@id;
                DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;
                DELETE FROM restaurants WHERE id=@id
                """, new { id });
        }
    }

    [Fact]
    public async Task RecipeDetailsCarryTheirCanonicalUrl()
    {
        using var app = Factory();
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client = CreateClient(app);

        // Receita hermética: criada e publicada pelo próprio fluxo editorial, removida ao final.
        var marker = Guid.NewGuid().ToString("N");
        var recipe = new ParaRecipe
        {
            Slug = "fixture-recipe-" + marker,
            Title = "Receita " + marker,
            Subtitle = "Adaptação de homologação",
            Story = "Conteúdo temporário de teste, removido ao final.",
            Ingredients = ["200 g de cupuaçu"],
            Steps = ["Separar os ingredientes.", "Preparar com cuidado."],
            PrepMinutes = 10,
            CookMinutes = 5
        };
        var recipes = scope.ServiceProvider.GetRequiredService<IParaRecipes>();
        try
        {
            await recipes.SaveAsync(recipe, "fixture-admin", default);
            recipe.Status = "published";
            await recipes.SaveAsync(recipe, "fixture-admin", default);

            var response = await client.GetAsync("/receitas/" + recipe.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal("https://localhost/receitas/" + recipe.Slug, OgAttribute(html, "og:url"));
            Assert.Equal("article", OgAttribute(html, "og:type"));

            // Sem fotografia real a receita não promete imagem: o cartão do Twitter desce para "summary".
            Assert.Contains("<meta name=\"twitter:card\" content=\"summary\" />", html);
        }
        finally
        {
            var rid = await db.ExecuteScalarAsync<long?>("SELECT id FROM para_recipes WHERE slug=@slug", new { slug = recipe.Slug });
            await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject='fixture-admin' AND entity_type='recipe' AND entity_id=@id", new { id = rid });
            await db.ExecuteAsync("DELETE FROM para_recipes WHERE slug=@slug", new { slug = recipe.Slug });
        }
    }
}
