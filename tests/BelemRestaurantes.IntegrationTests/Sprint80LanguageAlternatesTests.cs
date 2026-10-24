using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 80 — os idiomas do site ganham endereço: ?lang=xx resolve a língua de qualquer página
// pública (cookie > URL > Accept-Language > padrão), e cada página publica os alternates hreflang
// apenas para idiomas com tradução própria verificada (pt-BR, en, es) mais o x-default. O sitemap
// continua em língua única nesta Sprint (a multiplicação das URLs fica para o momento do shard).
[Collection("MySql")]
public sealed class Sprint80LanguageAlternatesTests
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

    private static string Alternate(string html, string tag)
    {
        var match = Regex.Match(html, $"<link rel=\"alternate\" hreflang=\"{Regex.Escape(tag)}\" href=\"([^\"]*)\"");
        Assert.True(match.Success, $"não achei o link rel=\"alternate\" hreflang=\"{tag}\"");
        return match.Groups[1].Value;
    }

    private static async Task<string> HtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EveryPublicPagePublishesTheTranslatedLanguagesAndXDefault()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var home = await HtmlAsync(client, "/");

        Assert.Equal("https://localhost/", Alternate(home, "pt-br"));
        Assert.Equal("https://localhost/?lang=en", Alternate(home, "en"));
        Assert.Equal("https://localhost/?lang=es", Alternate(home, "es"));
        // x-default é a página de registro: a própria home em português, sem parâmetro.
        Assert.Equal("https://localhost/", Alternate(home, "x-default"));
    }

    [Fact]
    public async Task TheEnglishCatalogPageServesEnglishAndPointsToItself()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/cidade/belem?lang=en");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", response.Content.Headers.ContentLanguage?.FirstOrDefault());
        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("<html lang=\"en\"", html);
        // O cartão compartilhado da página inglesa aponta para a própria língua, não para a PT.
        var ogMatch = Regex.Match(html, "<meta property=\"og:url\" content=\"([^\"]*)\"");
        Assert.Equal("https://localhost/cidade/belem?lang=en", ogMatch.Groups[1].Value);
        // Os alternates seguem anunciando o conjunto completo a partir desta página.
        Assert.Equal("https://localhost/cidade/belem", Alternate(html, "pt-br"));
        Assert.Equal("https://localhost/cidade/belem?lang=en", Alternate(html, "en"));
    }

    [Fact]
    public async Task AnUnsupportedLanguageParameterFallsThroughToPortuguese()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/cidade/belem?lang=xx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pt-BR", response.Content.Headers.ContentLanguage?.FirstOrDefault());

        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var ogMatch = Regex.Match(html, "<meta property=\"og:url\" content=\"([^\"]*)\"");
        Assert.Equal("https://localhost/cidade/belem", ogMatch.Groups[1].Value);
    }

    [Fact]
    public async Task TheBrowserAcceptLanguageFallbackStillSwitches()
    {
        using var app = Factory();
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var response = await client.GetAsync("/cidade/belem");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", response.Content.Headers.ContentLanguage?.FirstOrDefault());

        var html = HttpUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("<html lang=\"en\"", html);
    }

    [Fact]
    public async Task TheSitemapStaysSingleLanguageUntilSharding()
    {
        using var app = Factory();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var xml = await response.Content.ReadAsStringAsync();

        // A cidade continua indexada sem sufixo; nenhuma variante ?lang entra ainda.
        Assert.Contains("<loc>https://localhost/cidade/belem</loc>", xml);
        Assert.DoesNotContain("?lang=", xml);
    }
}
