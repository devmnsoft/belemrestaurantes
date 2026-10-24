using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Sprint 76 — uma entrada dinâmica de <c>/sitemap.xml</c>: rota pública (sempre iniciando
/// por "/") e, quando conhecida com honestidade, a última modificação do conteúdo. Caminhos estáveis
/// do portal entram com <see cref="LastModifiedUtc"/> nulo — sitemap aceita a omissão.</summary>
public sealed record SitemapEntry(string Path, DateTimeOffset? LastModifiedUtc);

/// <summary>Sprint 76 — fonte somente leitura do conteúdo dinâmico do <c>/sitemap.xml</c>.
/// Cada seção expõe exatamente o que já responde no portal público (mesmo predicado das rotas);
/// nenhuma URL é inventada fora desses conjuntos.</summary>
public interface ISitemapSource
{
    /// <summary>Estabelecimentos publicados (<c>/estabelecimento/{id}</c>), paginados por chave para caber em memória estável.</summary>
    Task<IReadOnlyList<SitemapEntry>> EstablishmentsAsync(CancellationToken ct);

    /// <summary>Receitas publicadas (<c>/receitas/{slug}</c>).</summary>
    Task<IReadOnlyList<SitemapEntry>> RecipesAsync(CancellationToken ct);

    /// <summary>Roteiros ativos e publicados no portal (<c>/roteiros/{slug}</c>), mesmo predicado da rota pública.</summary>
    Task<IReadOnlyList<SitemapEntry>> TrailsAsync(CancellationToken ct);

    /// <summary>Municípios com página pública (<c>/cidade/{slug}</c>), slug idêntico ao dos links internos.</summary>
    Task<IReadOnlyList<SitemapEntry>> CitiesAsync(CancellationToken ct);

    /// <summary>Localidades turísticas com página pública (<c>/localidade/{slug}</c>).</summary>
    Task<IReadOnlyList<SitemapEntry>> LocalitiesAsync(CancellationToken ct);

    /// <summary>Categorias que existem entre os publicados (<c>/categoria/{slug}</c>) — sem categoria não há página útil.</summary>
    Task<IReadOnlyList<SitemapEntry>> CategoriesAsync(CancellationToken ct);
}
