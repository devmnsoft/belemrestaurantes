using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Application.Services;

/// <summary>Sprint 76 — geração determinística de <c>sitemap.xml</c> e <c>robots.txt</c>.
/// Função pura: recebe a origem HTTPS do portal e as entradas e devolve o texto exato, o que torna
/// o resultado testável sem banco nem HTTP. Regras:
/// <list type="bullet">
/// <item><c><loc></c> sempre absoluto na origem configurada (nunca em host relativo);</item>
/// <item><c><lastmod></c> só quando conhecido, no subconjunto W3C aceito por buscadores (UTC com "Z");</item>
/// <item>um arquivo único comporta até <see cref="MaxUrlsPerFile"/> URLs (teto do protocolo); acima disso
/// o site precisa de um sitemap index — falhar alto é mais honesto do que truncar em silêncio;</item>
/// <item>texto XML escapado (&lt;, &gt;, &amp;) para slugs com caracteres especiais.</item>
/// </list></summary>
public static class SitemapBuilder
{
    public const int MaxUrlsPerFile = 50_000;

    /// <summary>Origem padrão do portal: a mesma origem pública oficial já usada nos links transacionais.</summary>
    public const string DefaultPublicOrigin = "https://belemrestaurantes.com.br";

    /// <summary>Prefíxos de rota fora do escopo de indexação pública. Mantidos alinhados ao conteúdo dos Disallow.</summary>
    public static readonly IReadOnlyList<string> DisallowedPrefixes =
    [
        "/admin", "/api", "/account", "/partner", "/language", "/dev"
    ];

    /// <summary>Resolve a origem pública: configuração <c>Seo:PublicOrigin</c> ou a origem oficial padrão.
    /// Sucessora direta da regra "origem deve usar HTTPS" dos links da área de membros.</summary>
    public static string ResolvePublicOrigin(string? configured)
    {
        var origin = string.IsNullOrWhiteSpace(configured) ? DefaultPublicOrigin : configured.Trim().TrimEnd('/');
        if (!origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A origem pública do site deve usar HTTPS.", nameof(configured));
        }
        return origin;
    }

    public static string Build(string origin, IReadOnlyCollection<SitemapEntry> entries)
    {
        var safe = ResolvePublicOrigin(origin);
        if (entries.Count > MaxUrlsPerFile)
        {
            throw new InvalidOperationException(
                $"O sitemap possui {entries.Count.ToString(CultureInfo.InvariantCulture)} URLs, acima do teto de {MaxUrlsPerFile.ToString(CultureInfo.InvariantCulture)} por arquivo. Crie um sitemap index antes de publicar.");
        }

        var builder = new StringBuilder(256 + entries.Count * 96);
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var entry in entries)
        {
            AppendUrl(builder, safe, entry);
        }
        builder.Append("</urlset>\n");
        return builder.ToString();
    }

    internal static void AppendUrl(StringBuilder builder, string origin, SitemapEntry entry)
    {
        builder.Append("  <url><loc>").Append(XmlEscape(origin + entry.Path)).Append("</loc>");
        if (entry.LastModifiedUtc is { } modified)
        {
            builder.Append("<lastmod>")
                .Append(modified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append("</lastmod>");
        }
        builder.Append("</url>\n");
    }

    public static string RobotsTxt(string origin)
    {
        var safe = ResolvePublicOrigin(origin);
        var builder = new StringBuilder(384);
        builder.Append("User-agent: *\nAllow: /\n");
        foreach (var prefix in DisallowedPrefixes)
        {
            builder.Append("Disallow: ").Append(prefix).Append('\n');
        }
        builder.Append('\n').Append("Sitemap: ").Append(safe).Append("/sitemap.xml\n");
        return builder.ToString();
    }

    /// <summary>Escapamento mínimo e suficiente para conteúdo de <c><loc></c>/<c><lastmod></c>:
    /// os cinco caracteres especiais do XML. Ordem importa: "&amp;" primeiro.</summary>
    public static string XmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
