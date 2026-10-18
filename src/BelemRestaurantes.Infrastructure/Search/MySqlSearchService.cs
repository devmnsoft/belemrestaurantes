using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Search;

public sealed class MySqlSearchService(MySqlConnectionFactory connectionFactory) : ISearchService
{
    public async Task<IReadOnlyList<AutocompleteResult>> AutocompleteAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return Array.Empty<AutocompleteResult>();
        }

        var cleanQuery = query.Trim();
        var term = $"%{cleanQuery}%";
        var results = new List<AutocompleteResult>();

        await using var connection = await connectionFactory.OpenAsync(ct);

        // 1. Restaurantes publicados por Nome ou Categoria
        const string resSql = @"
            SELECT id, name, category, neighborhood, city
            FROM restaurants
            WHERE status = 'published' AND (name LIKE @term OR category LIKE @term OR neighborhood LIKE @term)
            LIMIT 5;";

        var resList = await connection.QueryAsync<(long id, string name, string? category, string? neighborhood, string city)>(
            new CommandDefinition(resSql, new { term }, cancellationToken: ct));

        foreach (var r in resList)
        {
            var subtitle = $"{r.category ?? "Restaurante"} • {r.neighborhood ?? "Centro"}, {r.city}";
            results.Add(new AutocompleteResult("restaurant", r.id, r.name, subtitle, $"/estabelecimento/{r.id}"));
        }

        // 2. Pratos do cardápio digital
        const string dishSql = @"
            SELECT m.id, m.name as dishName, m.price, r.id as resId, r.name as resName, r.city
            FROM menu_items m
            JOIN restaurants r ON r.id = m.restaurant_id
            WHERE r.status = 'published' AND m.is_available = TRUE AND (m.name LIKE @term OR m.ingredients LIKE @term)
            LIMIT 3;";

        var dishList = await connection.QueryAsync<(long id, string dishName, decimal price, long resId, string resName, string city)>(
            new CommandDefinition(dishSql, new { term }, cancellationToken: ct));

        foreach (var d in dishList)
        {
            var subtitle = $"Prato em {d.resName} • R$ {d.price:F2}";
            results.Add(new AutocompleteResult("dish", d.id, d.dishName, subtitle, $"/cardapio/{d.resId}"));
        }

        return results;
    }

    public async Task<SearchFilterOptions> GetFilterOptionsAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        const string sql = @"
            SELECT DISTINCT category FROM restaurants WHERE status = 'published' AND category IS NOT NULL AND category != '' ORDER BY category;
            SELECT DISTINCT neighborhood FROM restaurants WHERE status = 'published' AND neighborhood IS NOT NULL AND neighborhood != '' ORDER BY neighborhood;";

        await using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: ct));
        var cities = BelemRestaurantes.Domain.Data.ParaOfficialCatalog.Municipalities.Select(c=>c.Name).OrderBy(n=>n).ToArray();
        var categories = (await multi.ReadAsync<string>()).ToList();
        var neighborhoods = (await multi.ReadAsync<string>()).ToList();

        return new SearchFilterOptions(cities, categories, neighborhoods);
    }
}
