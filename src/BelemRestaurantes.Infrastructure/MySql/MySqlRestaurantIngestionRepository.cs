using Dapper;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Escrita do catálogo a partir das fontes externas.
/// Invariante central: <c>status</c> nasce <c>pending</c> e nenhum método
/// desta classe o altera. Publicação só existe em <see cref="MySqlModerationRepository"/>.
/// </summary>
public sealed class MySqlRestaurantIngestionRepository(MySqlConnectionFactory factory) : IRestaurantIngestionRepository
{
    public async Task<IReadOnlyList<DeduplicationCandidate>> FindDuplicationCandidatesAsync(
        string normalizedName,
        string city,
        string? externalId,
        CancellationToken ct)
    {
        var normalizedCity = NameNormalizer.Normalize(city);

        await using var connection = await factory.OpenAsync(ct);

        // Janela ampla de propósito: a comparação final é feita pela
        // DeduplicationService, que sabe ponderar similaridade e Place ID.
        var rows = await connection.QueryAsync<CandidateRow>(new CommandDefinition(
            """
            SELECT id AS Id, normalized_name AS NormalizedName, normalized_city AS NormalizedCity,
                   google_place_id AS GooglePlaceId
            FROM restaurants
            WHERE normalized_city = @normalizedCity
              AND (normalized_name = @normalizedName
                   OR normalized_name LIKE @prefix
                   OR google_place_id = @externalId)
            LIMIT 200
            """,
            new
            {
                normalizedCity,
                normalizedName,
                prefix = PrefixOf(normalizedName),
                externalId
            },
            cancellationToken: ct));

        return rows
            .Select(r => new DeduplicationCandidate(r.Id, r.NormalizedName, r.NormalizedCity, r.GooglePlaceId))
            .ToArray();
    }

    public async Task<long> UpsertDiscoveredAsync(
        DiscoveredPlace place,
        string provider,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalizedName = NameNormalizer.Normalize(place.Name);
        var normalizedCity = NameNormalizer.Normalize(place.ResolvedCity);

        await using var connection = await factory.OpenAsync(ct);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Chave de deduplicação: Place ID quando existe, senão nome normalizado
        // + cidade. O status nunca entra no ON DUPLICATE KEY UPDATE.
        const string sql = """
            INSERT INTO restaurants (
                name, normalized_name, city, normalized_city, neighborhood, neighborhood_normalized,
                category, address, latitude, longitude, website, google_place_id, created_source_url,
                google_rating, google_rating_count, status, last_seen_at)
            VALUES (
                @name, @normalizedName, @city, @normalizedCity, @neighborhood, @neighborhoodNormalized,
                @category, @address, @latitude, @longitude, @website, @googlePlaceId, @sourceUrl,
                @rating, @ratingCount, 'pending', @now)
            ON DUPLICATE KEY UPDATE
                id = LAST_INSERT_ID(id),
                name = VALUES(name),
                normalized_name = VALUES(normalized_name),
                neighborhood = COALESCE(VALUES(neighborhood), neighborhood),
                neighborhood_normalized = COALESCE(VALUES(neighborhood_normalized), neighborhood_normalized),
                category = COALESCE(VALUES(category), category),
                address = COALESCE(VALUES(address), address),
                latitude = COALESCE(VALUES(latitude), latitude),
                longitude = COALESCE(VALUES(longitude), longitude),
                website = COALESCE(VALUES(website), website),
                google_rating = VALUES(google_rating),
                google_rating_count = VALUES(google_rating_count),
                last_seen_at = VALUES(last_seen_at)
            """;

        var parameters = new
        {
            name = place.Name.Trim(),
            normalizedName,
            city = place.ResolvedCity,
            normalizedCity,
            neighborhood = NeighborhoodRules.Clean(place.Neighborhood),
            neighborhoodNormalized = NameNormalizer.Normalize(place.Neighborhood ?? string.Empty),
            category = CategoryRules.Clean(place.Category),
            address = AddressRules.Clean(place.Address),
            latitude = place.Latitude,
            longitude = place.Longitude,
            website = SocialLinkRules.TryValidate(place.Website, out var site) ? site : null,
            googlePlaceId = place.ExternalId,
            sourceUrl = Truncate(place.GoogleMapsUri, 350),
            rating = place.GoogleRating,
            ratingCount = place.GoogleRatingCount,
            now
        };

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));

        // LAST_INSERT_ID(id) no UPSERT devolve o id do registro atualizado.
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<DataQuality> RecordObservationAsync(
        long restaurantId,
        string provider,
        string externalId,
        string? sourceUrl,
        string payloadHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO source_observations (
                restaurant_id, provider, external_id, source_url,
                first_seen_at, last_seen_at, last_fetched_at, data_quality, payload_hash)
            VALUES (@restaurantId, @provider, @externalId, @sourceUrl, @now, @now, @now, 'unverified', @payloadHash)
            ON DUPLICATE KEY UPDATE
                restaurant_id = VALUES(restaurant_id),
                source_url = COALESCE(VALUES(source_url), source_url),
                last_seen_at = VALUES(last_seen_at),
                last_fetched_at = VALUES(last_fetched_at),
                payload_hash = VALUES(payload_hash)
            """;

        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            restaurantId,
            provider,
            externalId,
            sourceUrl = Truncate(sourceUrl, 350),
            payloadHash,
            now
        }, cancellationToken: ct));

        var quality = await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT data_quality FROM source_observations WHERE provider=@provider AND external_id=@externalId",
            new { provider, externalId },
            cancellationToken: ct));

        return Enum.TryParse<DataQuality>(quality, ignoreCase: true, out var parsed) ? parsed : DataQuality.Unverified;
    }

    public async Task<IReadOnlyList<SourceObservation>> GetObservationsAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<ObservationRow>(new CommandDefinition(
            """
            SELECT id AS Id, restaurant_id AS RestaurantId, provider AS Provider, external_id AS ExternalId,
                   source_url AS SourceUrl, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt,
                   data_quality AS DataQuality, payload_hash AS PayloadHash
            FROM source_observations WHERE restaurant_id = @restaurantId
            """,
            new { restaurantId },
            cancellationToken: ct));

        return rows.Select(r => new SourceObservation(
            r.Id, r.RestaurantId, r.Provider, r.ExternalId, r.SourceUrl,
            r.FirstSeenAt, r.LastSeenAt,
            Enum.TryParse<DataQuality>(r.DataQuality, true, out var q) ? q : DataQuality.Unverified,
            null,
            r.PayloadHash)).ToArray();
    }

    /// <summary>
    /// Prefixa a busca por nome: "boi azul" também encontra "boi azul ii".
    /// Limitado aos 8 primeiros tokens para o índice continuar seletivo.
    /// </summary>
    private static string PrefixOf(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return " impossivel";
        }

        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var take = Math.Min(tokens.Length, 8);
        return string.Join(' ', tokens[..take]);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private sealed record CandidateRow(long Id, string NormalizedName, string NormalizedCity, string? GooglePlaceId);

    private sealed record ObservationRow(
        long Id, long RestaurantId, string Provider, string ExternalId, string? SourceUrl,
        DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, string DataQuality, string? PayloadHash);
}