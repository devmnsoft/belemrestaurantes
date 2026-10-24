using BelemRestaurantes.Application.Services;
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
        using var client = CreateClient(app);

        var response = await client.GetAsync("/categoria/loja-de-alimentos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("https://localhost/categoria/loja-de-alimentos", OgAttribute(html, "og:url"));
        Assert.Equal("Loja de Alimentos no Pará | Belém Restaurantes", OgAttribute(html, "og:title"));

        // Sprint 79 — o nome exibido é o rótulo guardado no catálogo (a rota aceita slug minúsculo).
        var count = await CountAsync(app, "SELECT COUNT(*) FROM restaurants WHERE status='published' AND category='Loja de Alimentos'");
        Assert.True(count > 0, "categoria de teste sem publicações — escolha outra categoria fixa");
        Assert.Equal(CatalogPageSeo.Description(count, "category", "Loja de Alimentos"), OgAttribute(html, "og:description"));
    }

    [Fact]
    public async Task RecipeDetailsCarryTheirCanonicalUrl()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/receitas/acai-tradicional-com-farinha");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal("https://localhost/receitas/acai-tradicional-com-farinha", OgAttribute(html, "og:url"));
        Assert.Equal("article", OgAttribute(html, "og:type"));

        // Sem fotografia real a receita não promete imagem: o cartão do Twitter desce para "summary".
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary\" />", html);
    }
}
