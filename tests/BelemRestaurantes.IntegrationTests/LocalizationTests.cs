using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using BelemRestaurantes.Web;
using BelemRestaurantes.Web.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class LocalizationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() ?? "Server=127.0.0.1;Database=belrestaurante;User=test;Password=test;SslMode=Required;"
                });
            });
        });

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Theory]
    [InlineData("pt-BR", "Buscar")]
    [InlineData("en", "Search")]
    [InlineData("fr", "Rechercher")]
    [InlineData("es", "Buscar")]
    [InlineData("ru", "Поиск")]
    [InlineData("de", "Suchen")]
    [InlineData("ja", "検索")]
    [InlineData("zh-Hans", "搜索")]
    public async Task Page_and_javascript_messages_use_requested_language(string culture, string search)
    {
        using var client = Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{culture}\">", html);
        Assert.Contains($"id=\"go\">{search}</button>", html);
        Assert.Contains("id=\"client-text\"", html);
        Assert.Contains(culture, response.Content.Headers.ContentLanguage);
    }

    [Theory]
    [InlineData("pt-BR","À flor do paladar.","Antes de sair de casa")]
    [InlineData("en","A taste of the Amazon.","Before you go")]
    [InlineData("es","Un viaje de sabores.","Antes de salir")]
    public async Task EditorialHomeGuideAndDestinationFilterWorkInMainVisitorLanguages(string culture,string headline,string visit)
    {
        using var client=Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var home=WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains(headline,home);Assert.DoesNotContain("AtlasHeadline",home);
        var guide=WebUtility.HtmlDecode(await client.GetStringAsync("/guia"));
        Assert.Contains(visit,guide);
        var cities=WebUtility.HtmlDecode(await client.GetStringAsync("/cidades?q=santarem"));
        Assert.Contains("/cidade/santarem",cities);Assert.DoesNotContain("/cidade/soure",cities);
        var empty=await client.GetStringAsync("/cidades?region=RegiaoInexistente");
        Assert.DoesNotContain("class=\"atlas-city-card\"",empty);
    }

    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("fr-CA", "fr")]
    [InlineData("en-US", "en")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("en;q=0,fr;q=0.8", "fr")]
    public async Task Regional_browser_preferences_and_quality_are_respected(string preference, string culture)
    {
        using var client = Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(preference);
        Assert.Contains($"<html lang=\"{culture}\">", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Choice_persists_and_takes_precedence_over_browser_language()
    {
        using var client = Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        var response = await ChangeLanguage(client, "ja", "/#catalogo");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/#catalogo", response.Headers.Location?.ToString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("<html lang=\"ja\">", html);
        Assert.Contains("検索", html);
    }

    [Fact]
    public async Task Public_navigation_keeps_five_plain_actions_and_the_rest_nearby()
    {
        using var client = Client();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("Onde comer?", html);
        Assert.Contains("O que comer?", html);
        Assert.Contains("Como chegar?", html);
        Assert.Contains("Receitas do Pará", html);
        Assert.Contains("Meus favoritos", html);
        Assert.Contains("href=\"/busca\"", html);
        Assert.Contains("href=\"/pratos\"", html);
        Assert.Contains("href=\"/cuia-rotas\"", html);
        Assert.Contains("href=\"/receitas\"", html);
        Assert.Contains("href=\"/account/favorites\"", html);
        Assert.Contains("Cuia IA", html);
        Assert.Contains(">Mais<", html);
    }

    [Fact]
    public async Task Unsupported_browser_language_falls_back_to_portuguese()
    {
        using var client = Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");
        Assert.Contains("<html lang=\"pt-BR\">", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Invalid_choice_is_rejected_and_external_redirect_is_not_followed()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeLanguage(client, "../../invalid", "/")).StatusCode);
        var response = await ChangeLanguage(client, "en", "https://example.invalid/");
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Change_requires_csrf_token()
    {
        using var client = Client();
        using var response = await client.PostAsync("/language", new FormUrlEncodedContent(new Dictionary<string, string> { ["culture"] = "fr" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Every_language_has_the_same_resource_keys_without_portuguese_fallback()
    {
        using var scope = factory.Services.CreateScope();
        var text = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            string[]? keys = null;
            foreach (var language in SupportedLanguages.All)
            {
                System.Globalization.CultureInfo.CurrentUICulture = new(language.Code);
                var entries = text.GetAllStrings(includeParentCultures: false).OrderBy(entry => entry.Name).ToArray();
                Assert.True(entries.Length > 60);
                Assert.All(entries, entry => Assert.False(entry.ResourceNotFound));
                var current = entries.Select(entry => entry.Name).ToArray();
                if (keys is not null) Assert.Equal(keys, current);
                keys = current;
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = original; }
    }

    private static async Task<HttpResponseMessage> ChangeLanguage(HttpClient client, string culture, string returnUrl)
    {
        var html = await client.GetStringAsync("/");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return await client.PostAsync("/language", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["culture"] = culture, ["returnUrl"] = returnUrl, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }));
    }

    public async Task InitializeAsync()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();
        await services.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();
}
