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
               business_status AS BusinessStatus, created_source_url AS SourceUrl
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

    /// <summary>
    /// Escapa os curingas do LIKE. Sem isso, o usuário searching por '%' ou '_'
    /// causaria varredura total da tabela.
    /// </summary>
    internal static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
