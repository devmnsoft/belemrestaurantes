using System;
using System.Collections.Generic;
using System.Globalization;

namespace BelemRestaurantes.Application.Services;

/// <summary>Sprint 80 — alternância de idiomas legível por buscadores (hreflang). Função pura,
/// testável sem banco nem HTTP. Regras de honestidade da casa:
/// <list type="bullet">
/// <item>somente os idiomas com <b>tradução própria verificada</b> nos recursos ganham endereço
/// (pt-BR, en, es) — os demais resx mantêm o fallback editorial inglês que o site informa, e
/// declarar tradução onde não há seria inventar;</item>
/// <item><b>x-default</b> aponta para a página de registro (pt-BR, sem parâmetro); cada idioma
/// não-padrão aponta para a <b>mesma</b> página com <c>?lang=xx</c> (o parâmetro lido pela
/// <see cref="QueryLanguageProvider"/> equivalente em Web);</item>
/// <item>a URL canônica de compartilhamento (og:url) de uma página fora do português recebe o
/// sufixo uma única vez, para o cartão apontar para a própria língua;</item>
/// <item>a origem absoluta passa pela mesma régua HTTPS do sitemap.</item>
/// </list></summary>
public static class HreflangBuilder
{
    /// <summary>Idiomas com texto próprio verificado nos recursos SharedResource.*.resx.</summary>
    public static readonly IReadOnlyList<string> TranslatedCultures = ["pt-BR", "en", "es"];

    /// <summary>Cultura de registro do portal (audience de referência para x-default).</summary>
    public const string DefaultCulture = "pt-BR";

    /// <summary>Tag hreflang BCP-47 em minúsculas ("pt-br", "en", "es").</summary>
    public static string Tag(string culture) => culture.ToLowerInvariant();

    /// <summary>URL absoluta do idioma para a página indicada. O idioma de registro usa a página
    /// sem parâmetro; os demais usam a mesma página com <c>?lang=xx</c>.</summary>
    public static string AlternateUrl(string origin, string path, string culture)
    {
        EnsureTranslated(culture);
        EnsurePath(path);
        var absolute = SitemapBuilder.ResolvePublicOrigin(origin) + path;
        return string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase)
            ? absolute
            : absolute + "?lang=" + culture.ToLowerInvariant();
    }

    /// <summary>Conjunto completo para o <c>&lt;head&gt;</c>: uma entrada por idioma traduzido, na
    /// ordem fixa, mais o <c>x-default</c> apontando para a página de registro.</summary>
    public static IReadOnlyList<(string Tag, string Url)> Alternates(string origin, string path)
    {
        var list = new List<(string, string)>(TranslatedCultures.Count + 1);
        foreach (var culture in TranslatedCultures)
        {
            list.Add((Tag(culture), AlternateUrl(origin, path, culture)));
        }
        list.Add(("x-default", AlternateUrl(origin, path, DefaultCulture)));
        return list;
    }

    /// <summary>og:url auto-referente por cultura. Páginas em português permanecem exatas; as
    /// demais recebem <c>?lang=&lt;cultura&gt;</c> — idempotente se o sufixo já estiver presente.</summary>
    public static string OgUrlForCulture(string ogUrl, string cultureName)
    {
        if (string.IsNullOrWhiteSpace(ogUrl))
        {
            throw new ArgumentException("A URL canônica compartilhada não pode ser vazia.", nameof(ogUrl));
        }

        var twoLetter = new CultureInfo(cultureName).TwoLetterISOLanguageName;
        if (string.Equals(twoLetter, "pt", StringComparison.OrdinalIgnoreCase))
        {
            return ogUrl;
        }

        var suffix = "?lang=" + cultureName.ToLowerInvariant();
        return ogUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? ogUrl : ogUrl + suffix;
    }

    private static void EnsureTranslated(string culture)
    {
        if (!TranslatedCultures.Contains(culture))
        {
            throw new ArgumentException($"Idioma sem tradução própria não tem endereço hreflang: '{culture}'.", nameof(culture));
        }
    }

    private static void EnsurePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("O caminho alternativo deve ser absoluto dentro do site (começando com '/').", nameof(path));
        }
        if (path.Contains('?'))
        {
            throw new ArgumentException("O caminho alternativo não carrega consulta; o idioma entra apenas como parâmetro próprio.", nameof(path));
        }
    }
}
