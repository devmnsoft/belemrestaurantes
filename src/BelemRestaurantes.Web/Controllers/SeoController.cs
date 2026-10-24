using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Web.Controllers;

/// <summary>Sprint 76 — indexação do portal: <c>robots.txt</c> e <c>/sitemap.xml</c> gerados a partir do
/// conteúdo efetivamente público. O sitemap lista somente rotas que respondem 200 anônimo (estáticas,
/// verificadas por teste) e conteúdo publicado (dinâmico, com o mesmo predicado das rotas). A origem
/// absoluta vem de <c>Seo:PublicOrigin</c> (HTTPS obrigatória), com padrão na origem oficial.</summary>
[AllowAnonymous]
public sealed class SeoController(ISitemapSource sitemapSource, IConfiguration configuration) : Controller
{
    /// <summary>Rotas estáveis do portal público. Cada caminho responde 200 anônimo — os testes de
    /// integração percorrem a lista inteira e quebram o build se uma sair do ar.</summary>
    public static readonly IReadOnlyList<string> StaticPaths =
    [
        "/", "/guia", "/selo-cuia", "/busca", "/cidades", "/pratos", "/ocasioes", "/cuia-rotas",
        "/travessias", "/receitas", "/culinaria", "/descobrir", "/meu-passeio", "/referencias",
        "/roteiros", "/mapa", "/community", "/feed", "/cuia-ia", "/dados-abertos",
        "/dados-abertos/openstreetmap", "/privacidade", "/termos"
    ];

    private string Origin => SitemapBuilder.ResolvePublicOrigin(configuration["Seo:PublicOrigin"]);

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        var entries = new List<SitemapEntry>(StaticPaths.Count + ParaTasteCatalog.Dishes.Count + ParaTasteCatalog.Occasions.Count);
        foreach (var path in StaticPaths)
        {
            entries.Add(new SitemapEntry(path, null));
        }
        // Catálogo editorial fixo de sabores paraenses (pratos e ocasiões).
        foreach (var dish in ParaTasteCatalog.Dishes)
        {
            entries.Add(new SitemapEntry("/pratos/" + dish.Slug, null));
        }
        foreach (var occasion in ParaTasteCatalog.Occasions)
        {
            entries.Add(new SitemapEntry("/ocasioes/" + occasion.Slug, null));
        }
        // Conteúdo publicado, na ordem: maior volume primeiro para o arquivo manter paginação previsível.
        entries.AddRange(await sitemapSource.EstablishmentsAsync(ct));
        entries.AddRange(await sitemapSource.RecipesAsync(ct));
        entries.AddRange(await sitemapSource.TrailsAsync(ct));
        entries.AddRange(await sitemapSource.CitiesAsync(ct));
        entries.AddRange(await sitemapSource.LocalitiesAsync(ct));
        entries.AddRange(await sitemapSource.CategoriesAsync(ct));

        return Content(SitemapBuilder.Build(Origin, entries), "application/xml", Encoding.UTF8);
    }

    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots() => Content(SitemapBuilder.RobotsTxt(Origin), "text/plain", Encoding.UTF8);
}
