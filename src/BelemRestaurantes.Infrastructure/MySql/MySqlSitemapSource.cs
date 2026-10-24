using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>Sprint 76 — fonte dinâmica do <c>/sitemap.xml</c>: consultas somente leitura com o mesmo
/// predicado que as rotas públicas usam para responder 200, para o sitemap nunca listar página que
/// o portal não expõe. Estabelecimentos paginam por chave (id) no padrão da exportação aberta.</summary>
public sealed class MySqlSitemapSource(MySqlConnectionFactory factory) : ISitemapSource
{
    private const int Page = 1000;

    public async Task<IReadOnlyList<SitemapEntry>> EstablishmentsAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var entries = new List<SitemapEntry>();
        for (long after = 0; ; )
        {
            var rows = (await db.QueryAsync<(long Id, DateTimeOffset UpdatedAt)>(new CommandDefinition(
                """SELECT id AS Id, updated_at AS UpdatedAt FROM restaurants WHERE status='published' AND id>@after ORDER BY id LIMIT @page""",
                new { after, page = Page }, cancellationToken: ct))).AsList();
            if (rows.Count == 0) break;
            foreach (var (id, updatedAt) in rows)
            {
                entries.Add(new SitemapEntry("/estabelecimento/" + id.ToString(CultureInfo.InvariantCulture), updatedAt));
            }
            if (rows.Count < Page) break;
            after = rows[^1].Id;
        }
        return entries;
    }

    public async Task<IReadOnlyList<SitemapEntry>> RecipesAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<(string Slug, DateTimeOffset UpdatedAt)>(new CommandDefinition(
            """SELECT slug AS Slug, updated_at AS UpdatedAt FROM para_recipes WHERE status='published' ORDER BY slug""",
            null, cancellationToken: ct))).AsList();
        var entries = new List<SitemapEntry>(rows.Count);
        foreach (var (slug, updatedAt) in rows)
        {
            entries.Add(new SitemapEntry("/receitas/" + slug, updatedAt));
        }
        return entries;
    }

    public async Task<IReadOnlyList<SitemapEntry>> TrailsAsync(CancellationToken ct)
    {
        // Mesmo predicado da rota /roteiros/{slug}: ativo e publicado (ou sem camada editorial ainda).
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<(string Slug, DateTimeOffset? ReviewedAt)>(new CommandDefinition(
            """SELECT slug AS Slug, reviewed_at AS ReviewedAt FROM gastronomic_trails WHERE is_active=TRUE AND (editorial_json IS NULL OR editorial_state='published') ORDER BY slug""",
            null, cancellationToken: ct))).AsList();
        var entries = new List<SitemapEntry>(rows.Count);
        foreach (var (slug, reviewedAt) in rows)
        {
            entries.Add(new SitemapEntry("/roteiros/" + slug, reviewedAt));
        }
        return entries;
    }

    public async Task<IReadOnlyList<SitemapEntry>> CitiesAsync(CancellationToken ct)
    {
        // Slug idêntico ao dos links internos: NormalizedName.Replace(" ", "-") (mesma regra da vitrine /cidades).
        // DISTINCT sobre o slug: o mapa de municípios ainda carrega pares duplicados (mesmo nome, IBGE divergente);
        // uma URL por lugar é o que o sitemap pode afirmar.
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT REPLACE(normalized_name,' ','-') FROM geo_municipalities WHERE normalized_name<>'' ORDER BY 1",
            null, cancellationToken: ct))).AsList();
        var entries = new List<SitemapEntry>(rows.Count);
        foreach (var slug in rows)
        {
            entries.Add(new SitemapEntry("/cidade/" + slug, null));
        }
        return entries;
    }

    public async Task<IReadOnlyList<SitemapEntry>> LocalitiesAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT REPLACE(normalized_name,' ','-') FROM geo_districts_localities WHERE is_tourist_highlight=TRUE AND normalized_name<>'' ORDER BY 1",
            null, cancellationToken: ct))).AsList();
        var entries = new List<SitemapEntry>(rows.Count);
        foreach (var slug in rows)
        {
            entries.Add(new SitemapEntry("/localidade/" + slug, null));
        }
        return entries;
    }

    public async Task<IReadOnlyList<SitemapEntry>> CategoriesAsync(CancellationToken ct)
    {
        // A página /categoria/{slug} devolve o slug como termo de busca e a comparação da busca é
        // case/acento-insensível (colação do schema): minúsculas + hífen nunca perde o casamento.
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<string>(new CommandDefinition(
            """SELECT DISTINCT category FROM restaurants WHERE status='published' AND category IS NOT NULL AND TRIM(category)<>'' ORDER BY category""",
            null, cancellationToken: ct))).AsList();
        var entries = new List<SitemapEntry>(rows.Count);
        foreach (var category in rows)
        {
            entries.Add(new SitemapEntry("/categoria/" + CategoryUrlSlug(category), null));
        }
        return entries;
    }

    private static string CategoryUrlSlug(string category) => category.Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
}
