using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlTasteGuide(MySqlConnectionFactory factory) : ITasteGuide
{
    private const string Columns = """
        r.id AS Id, r.name AS Name, r.city AS City, r.neighborhood AS Neighborhood,
        r.category AS Category, r.address AS Address, r.website AS Website,
        r.instagram AS Instagram, r.facebook AS Facebook, r.tiktok AS TikTok,
        r.youtube AS YouTube, r.photo_url AS PhotoUrl, r.updated_at AS UpdatedAt,
        r.latitude AS Latitude, r.longitude AS Longitude, r.phone AS Phone,
        r.business_status AS BusinessStatus, r.created_source_url AS SourceUrl,
        r.catalog_tier AS CatalogTier, r.photo_attribution AS PhotoAttribution,
        r.photo_license AS PhotoLicense
        """;

    public async Task<TasteGuidePage?> GetAsync(string slug, CancellationToken ct)
    {
        var entry = ParaTasteCatalog.Find(slug);
        if (entry is null) return null;
        var guide = await MatchAsync(entry, "guia", 24, ct);
        var directory = await MatchAsync(entry, "ficha", 12, ct);
        return new TasteGuidePage(entry, guide, directory);
    }

    private async Task<IReadOnlyList<RestaurantView>> MatchAsync(TasteEntry entry, string tier, int limit, CancellationToken ct)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("tier", tier);
        parameters.Add("dishType", entry.RegionalType);
        parameters.Add("limit", limit);
        for (var i = 0; i < entry.Aliases.Count; i++)
        {
            var name = "a" + i;
            clauses.Add($"(r.name LIKE @{name} OR r.category LIKE @{name} OR r.neighborhood LIKE @{name} OR EXISTS (SELECT 1 FROM menu_items m WHERE m.restaurant_id=r.id AND m.is_available=1 AND (m.name LIKE @{name} OR m.ingredients LIKE @{name})))");
            parameters.Add(name, "%" + MySqlRestaurantReadRepository.EscapeLike(entry.Aliases[i]) + "%");
        }
        if (entry.Kind == "prato")
            clauses.Add("EXISTS (SELECT 1 FROM menu_items m WHERE m.restaurant_id=r.id AND m.is_available=1 AND m.regional_dish_type=@dishType)");
        if (clauses.Count == 0) return [];
        await using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<RestaurantView>(new CommandDefinition($"""
            SELECT {Columns}
            FROM restaurants r
            WHERE r.status='published' AND r.catalog_tier=@tier AND ({string.Join(" OR ", clauses)})
            ORDER BY r.name, r.id
            LIMIT @limit
            """, parameters, cancellationToken: ct));
        return rows.AsList();
    }
}

public sealed class MySqlSearchGapWriter(MySqlConnectionFactory factory) : ISearchGapWriter
{
    public async Task RecordAsync(string? term, string? city, int resultCount, CancellationToken ct)
    {
        var clean = term?.Trim();
        if (string.IsNullOrWhiteSpace(clean) || clean.Length < 2 || resultCount > 0) return;
        if (clean.Length > 160) clean = clean[..160];
        var place = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (place?.Length > 80) place = place[..80];
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO search_gaps(term, city, result_count, created_at)
            VALUES(@clean, @place, @resultCount, UTC_TIMESTAMP(6))
            """, new { clean, place, resultCount }, cancellationToken: ct));
    }
}
