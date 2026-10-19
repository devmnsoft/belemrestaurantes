using Dapper;
using MySqlConnector;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Leitura pública do catálogo. Toda consulta é parametrizada e restrita a
/// <c>status='published'</c>: o status é condição obrigatória, nunca opcional.
/// </summary>
public sealed class MySqlRestaurantReadRepository(MySqlConnectionFactory factory) : IRestaurantReadRepository
{
    private const string SelectColumns =
        """
        SELECT id AS Id, name AS Name, city AS City, neighborhood AS Neighborhood,
               category AS Category, address AS Address, website AS Website,
               instagram AS Instagram, facebook AS Facebook, tiktok AS TikTok,
               youtube AS YouTube, photo_url AS PhotoUrl, updated_at AS UpdatedAt,
               latitude AS Latitude, longitude AS Longitude, phone AS Phone,
               business_status AS BusinessStatus, created_source_url AS SourceUrl,
               catalog_tier AS CatalogTier, photo_attribution AS PhotoAttribution,
               photo_license AS PhotoLicense
        FROM restaurants
        """;

    public async Task<PagedResult<RestaurantView>> SearchAsync(RestaurantSearchQuery query, CancellationToken ct)
    {
        var page = query.SafePage;
        var pageSize = query.SafePageSize;
        var offset = (page - 1) * pageSize;

        var where = new List<string> { "status = 'published'" };
        var parameters = new DynamicParameters();
        parameters.Add("offset", offset);
        parameters.Add("pageSize", pageSize);
        const string distance = "6371 * ACOS(LEAST(1, GREATEST(-1, COS(RADIANS(@latitude))*COS(RADIANS(latitude))*COS(RADIANS(longitude)-RADIANS(@longitude))+SIN(RADIANS(@latitude))*SIN(RADIANS(latitude)))))";
        var select = SelectColumns;
        if (query.HasLocation)
        {
            parameters.Add("latitude", query.Latitude);
            parameters.Add("longitude", query.Longitude);
            parameters.Add("radius", query.SafeRadiusKm);
            where.Add($"latitude IS NOT NULL AND longitude IS NOT NULL AND {distance} <= @radius");
            select = SelectColumns.Replace("FROM restaurants", $", {distance} AS DistanceKm FROM restaurants", StringComparison.Ordinal);
        }
        if (query.PhotosOnly) where.Add("photo_url IS NOT NULL AND photo_url <> ''");
        if (query.SafeTier is { } tier) { where.Add("catalog_tier = @tier"); parameters.Add("tier", tier); }
        // A ordenação vem de uma lista fechada; nunca incorpora SQL enviado pelo usuário.
        var order = query.SafeSort switch { "recent" => "updated_at DESC, id DESC", "city" => "city, name, id", "nearby" => "DistanceKm, name, id", _ => "name, id" };

        if (query.SafeTerm is { } term)
        {
            // LIKE com parâmetro: o usuário nunca entra concatenado no SQL.
            where.Add("(name LIKE @term OR category LIKE @term OR neighborhood LIKE @term OR city LIKE @term)");
            parameters.Add("term", $"%{EscapeLike(term)}%");
        }

        if (query.SafeCity is { } city)
        {
            where.Add("normalized_city = @normalizedCity");
            parameters.Add("normalizedCity", NameNormalizer.Normalize(city));
        }

        if (!string.IsNullOrWhiteSpace(query.Neighborhood))
        {
            where.Add("neighborhood_normalized = @neighborhood");
            parameters.Add("neighborhood", NameNormalizer.Normalize(query.Neighborhood));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            where.Add("category = @category");
            parameters.Add("category", CategoryRules.Clean(query.Category) ?? query.Category.Trim());
        }

        if (query.MinPrice is { } min)
        {
            where.Add("(price_max IS NULL OR price_max >= @minPrice)");
            parameters.Add("minPrice", (int)min);
        }

        if (query.MaxPrice is { } max)
        {
            where.Add("(price_min IS NULL OR price_min <= @maxPrice)");
            parameters.Add("maxPrice", (int)max);
        }

        var whereClause = string.Join(" AND ", where);

        await using var connection = await factory.OpenAsync(ct);

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM restaurants WHERE {whereClause}", parameters, cancellationToken: ct));

        if (total == 0)
        {
            return PagedResult<RestaurantView>.Empty(page, pageSize);
        }

        var items = await connection.QueryAsync<RestaurantView>(new CommandDefinition(
            $"""
             {select}
             WHERE {whereClause}
             ORDER BY {order}
             LIMIT @pageSize OFFSET @offset
             """,
            parameters,
            cancellationToken: ct));

        return new PagedResult<RestaurantView>(items.AsList(), page, pageSize, total);
    }

    public async Task<RestaurantView?> GetPublishedAsync(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        return await connection.QueryFirstOrDefaultAsync<RestaurantView>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @id AND status = 'published'",
            new { id },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> ListCitiesAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT city FROM restaurants WHERE status='published' ORDER BY city", cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> ListNeighborhoodsAsync(string city, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT neighborhood FROM restaurants
            WHERE status='published' AND normalized_city = @normalizedCity AND neighborhood IS NOT NULL
            ORDER BY neighborhood
            """,
            new { normalizedCity = NameNormalizer.Normalize(city) },
            cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT category FROM restaurants WHERE status='published' AND category IS NOT NULL ORDER BY category",
            cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyDictionary<string,int>> CountPublishedByCityAsync(CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);
        var rows=await connection.QueryAsync<(string City,int Total)>(new CommandDefinition(
            "SELECT normalized_city, COUNT(*) FROM restaurants WHERE status='published' GROUP BY normalized_city",cancellationToken:ct));
        return rows.ToDictionary(r=>r.City,r=>r.Total,StringComparer.Ordinal);
    }

    private const string MapWhere = "status = 'published' AND latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND NOT (ABS(latitude) < 0.000001 AND ABS(longitude) < 0.000001)";

    public async Task<IReadOnlyList<CatalogMapPoint>> ListPublishedMapAsync(CancellationToken ct)
        => (await SearchMapAsync(new MapQuery(), ct)).Points;

    public async Task<MapSummary> GetMapSummaryAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<(string City, int Total, int Guides)>(new CommandDefinition(
            $"SELECT city, COUNT(*), SUM(catalog_tier='guia') FROM restaurants WHERE {MapWhere} GROUP BY city ORDER BY city", cancellationToken: ct))).AsList();
        return new(rows.Sum(r => r.Total), rows.Sum(r => r.Guides), rows.Select(r => r.City).ToList());
    }

    public async Task<MapPointPage> SearchMapAsync(MapQuery query, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var where = new List<string> { MapWhere, "id > @after" };
        var parameters = new DynamicParameters(new { after = query.SafeAfter, take = query.SafeLimit + 1 });
        if (MapQuery.Clean(query.City) is {} city) { where.Add("normalized_city = @city"); parameters.Add("city", NameNormalizer.Normalize(city)); }
        if (MapQuery.Clean(query.Neighborhood) is {} neighborhood) { where.Add("neighborhood_normalized = @neighborhood"); parameters.Add("neighborhood", NameNormalizer.Normalize(neighborhood)); }
        if (MapQuery.Clean(query.Category) is {} category) { where.Add("category = @category"); parameters.Add("category", category); }
        if (query.Tier is "ficha" or "guia") { where.Add("catalog_tier = @tier"); parameters.Add("tier", query.Tier); }
        if (MapQuery.Clean(query.Term) is {} term) { where.Add("(name LIKE @term OR category LIKE @term OR neighborhood LIKE @term)"); parameters.Add("term", $"%{EscapeLike(term)}%"); }
        if (query.Bounds is {} bounds) {
            where.Add("latitude BETWEEN @south AND @north AND longitude BETWEEN @west AND @east");
            parameters.Add("south", bounds.South); parameters.Add("north", bounds.North); parameters.Add("west", bounds.West); parameters.Add("east", bounds.East);
        }
        await using var connection = await factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<CatalogMapPoint>(new CommandDefinition(
            $"SELECT id, name, city, neighborhood, category, latitude, longitude, catalog_tier FROM restaurants WHERE {string.Join(" AND ", where)} ORDER BY id LIMIT @take",
            parameters, cancellationToken: ct))).AsList();
        MapDiagnostics.QueryMilliseconds.Record(watch.Elapsed.TotalMilliseconds);
        var more = rows.Count > query.SafeLimit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows, more ? rows[^1].Id : null);
    }

    public async Task<CatalogFactSheet?> GetFactSheetAsync(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<CatalogFactRow>(new CommandDefinition(
            """
            SELECT
              (r.address IS NOT NULL AND r.address <> '') AS HasAddress,
              (r.latitude IS NOT NULL AND r.longitude IS NOT NULL) AS HasCoordinates,
              (r.phone IS NOT NULL AND r.phone <> '') AS HasPhone,
              (r.photo_url IS NOT NULL AND r.photo_url <> '') AS HasPhoto,
              (EXISTS (SELECT 1 FROM restaurant_schedules s WHERE s.restaurant_id = r.id)
                OR EXISTS (SELECT 1 FROM restaurant_operation_info i WHERE i.restaurant_id = r.id AND i.expires_at > UTC_TIMESTAMP(6))) AS HasHours,
              EXISTS (SELECT 1 FROM menu_items m WHERE m.restaurant_id = r.id) AS HasMenu,
              EXISTS (SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id = r.id) AS HasOwner
            FROM restaurants r
            WHERE r.id = @id AND r.status = 'published'
            """,
            new { id },
            cancellationToken: ct));
        return row is null ? null : new CatalogFactSheet(
            row.HasAddress != 0, row.HasCoordinates != 0, row.HasPhone != 0, row.HasPhoto != 0,
            row.HasHours != 0, row.HasMenu != 0, row.HasOwner != 0);
    }

    private sealed class CatalogFactRow
    {
        public int HasAddress { get; set; }
        public int HasCoordinates { get; set; }
        public int HasPhone { get; set; }
        public int HasPhoto { get; set; }
        public int HasHours { get; set; }
        public int HasMenu { get; set; }
        public int HasOwner { get; set; }
    }

    /// <summary>
    /// Escapa os curingas do LIKE. Sem isso, o usuário searching por '%' ou '_'
    /// causaria varredura total da tabela.
    /// </summary>
    internal static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
