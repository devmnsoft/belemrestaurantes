using System;
using System.Globalization;

namespace BelemRestaurantes.Application.Services;

/// <summary>Sprint 79 — cartão de compartilhamento das páginas de catálogo público
/// (/cidade/{slug}, /localidade/{slug}, /categoria/{slug}). Função pura: a view só informa o
/// contexto (tipo, nome exibido e contagem real no momento da renderização) e o caminho exato que
/// acabou de ser servido. Regra de honestidade da casa: a contagem aparece como está — zero tem
/// frase própria, número nenhum é arredondado nem decorado, e nada publicado além do que o catálogo
/// já responde 200.</summary>
public static class CatalogPageSeo
{
    /// <summary>URL canônica para og:url: origem pública configurada + o caminho servido, sem
    /// consulta. A paginação da mesma página aponta sempre para a primeira página.</summary>
    public static string OgUrl(string? origin, string requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath) || !requestPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("O caminho canônico deve ser absoluto dentro do site (começando com '/').", nameof(requestPath));
        }
        if (requestPath.IndexOf('?') >= 0)
        {
            throw new ArgumentException("A parte canônica da URL não carrega consulta; use apenas o caminho.", nameof(requestPath));
        }
        return SitemapBuilder.ResolvePublicOrigin(origin) + requestPath;
    }

    /// <summary>Título da página (sem o sufixo de marca — o layout acrescenta o sufixo do <c>&lt;title&gt;</c>).</summary>
    public static string Title(string kind, string name)
    {
        EnsureName(name);
        return IsPlace(kind) ? $"Restaurantes em {name}" : $"{name} no Pará";
    }

    /// <summary>Título do cartão compartilhado (og/twitter), com a marca.</summary>
    public static string ShareTitle(string kind, string name) => $"{Title(kind, name)} | Belém Restaurantes";

    /// <summary>Descrição construída apenas com o que o catálogo respondeu: a contagem real de
    /// publicações. Números em cultura invariante para leitura determinística por raspadores.</summary>
    public static string Description(int publishedCount, string kind, string name)
    {
        if (publishedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(publishedCount), "Contagem de publicações não é negativa.");
        }
        EnsureName(name);
        var n = publishedCount.ToString(CultureInfo.InvariantCulture);
        if (IsPlace(kind))
        {
            return publishedCount == 0
                ? $"Nenhum restaurante publicado em {name} até agora."
                : $"{n} restaurante{(publishedCount == 1 ? string.Empty : "s")} publicado{(publishedCount == 1 ? string.Empty : "s")} em {name}.";
        }
        return publishedCount == 0
            ? "Nenhum estabelecimento publicado nesta categoria até agora."
            : $"{n} estabelecimento{(publishedCount == 1 ? string.Empty : "s")} publicado{(publishedCount == 1 ? string.Empty : "s")} como {name}.";
    }

    private static bool IsPlace(string kind) => kind switch
    {
        "city" or "locality" => true,
        "category" => false,
        _ => throw new ArgumentException($"Tipo de página de catálogo desconhecido: '{kind}'.", nameof(kind))
    };

    private static void EnsureName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome exibido não pode ser vazio.", nameof(name));
        }
    }
}
