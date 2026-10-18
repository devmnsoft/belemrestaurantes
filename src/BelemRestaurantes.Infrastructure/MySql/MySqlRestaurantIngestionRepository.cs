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
                prefix = PrefixOf(normalizedName) + "%",
                externalId
            },
            cancellationToken: ct));

        return rows
            .Select(r => new DeduplicationCandidate(r.Id, r.NormalizedName, r.NormalizedCity, r.GooglePlaceId))
            .ToArray();
    }

    public async Task<long> UpsertDiscoveredAsync(DiscoveredPlace place, string provider, DateTimeOffset now, CancellationToken ct)
        => (await UpsertWithOutcomeAsync(place, provider, now, ct)).Id;

    public async Task<(long Id, bool Created)> UpsertWithOutcomeAsync(DiscoveredPlace place, string provider, DateTimeOffset now, CancellationToken ct)
    {
        var normalizedName = NameNormalizer.Normalize(place.Name);
        var normalizedCity = NameNormalizer.Normalize(place.ResolvedCity);

        await using var connection = await factory.OpenAsync(ct);
        var locked = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT GET_LOCK('belrestaurante:ingestion', 10)", cancellationToken: ct));
        if (locked != 1) throw new TimeoutException("Outra ingestão está ativa; tente novamente no próximo ciclo.");
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);

            // Place ID tem prioridade; nome/endereço exatos na mesma cidade evitam
            // duplicação entre fontes sem unir filiais de endereços distintos.
            var existingId = await connection.QueryFirstOrDefaultAsync<long?>(new CommandDefinition("""
                SELECT id FROM restaurants
                WHERE google_place_id = @externalId
                   OR (normalized_name = @normalizedName AND normalized_city = @normalizedCity
                       AND address IS NOT NULL AND address = @address)
                ORDER BY (google_place_id = @externalId) DESC LIMIT 1 FOR UPDATE
                """, new { externalId = place.ExternalId, normalizedName, normalizedCity,
                    address = AddressRules.Clean(place.Address) }, transaction, cancellationToken: ct));

            if (existingId is { } ownedId)
            {
                var verifiedOwner = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM restaurant_owners WHERE restaurant_id=@ownedId",
                    new { ownedId }, transaction, cancellationToken: ct));
                if (verifiedOwner > 0)
                {
                    // Fontes externas não sobrescrevem os dados de um parceiro verificado.
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE restaurants SET last_seen_at=@now WHERE id=@ownedId",
                        new { ownedId, now }, transaction, cancellationToken: ct));
                    await transaction.CommitAsync(ct);
                    return (ownedId, false);
                }
            }

            // Chave de deduplicação: Place ID quando existe, senão nome normalizado
            // + cidade. O status nunca entra no ON DUPLICATE KEY UPDATE.
            const string sql = """
                INSERT INTO restaurants (
                    id, name, normalized_name, city, normalized_city, neighborhood, neighborhood_normalized,
                    category, address, latitude, longitude, website, phone, instagram, facebook, tiktok, youtube, google_place_id, created_source_url,
                    google_rating, google_rating_count, business_status, status, last_seen_at)
                VALUES (
                    @existingId, @name, @normalizedName, @city, @normalizedCity, @neighborhood, @neighborhoodNormalized,
                    @category, @address, @latitude, @longitude, @website, @phone, @instagram, @facebook, @tiktok, @youtube, @googlePlaceId, @sourceUrl,
                    @rating, @ratingCount, COALESCE(@businessStatus, 'operational'), 'pending', @now)
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
                    business_status = COALESCE(@businessStatus, business_status),
                    phone = COALESCE(VALUES(phone), phone),
                    instagram = COALESCE(VALUES(instagram), instagram),
                    facebook = COALESCE(VALUES(facebook), facebook),
                    tiktok = COALESCE(VALUES(tiktok), tiktok),
                    youtube = COALESCE(VALUES(youtube), youtube),
                    google_place_id = COALESCE(google_place_id, VALUES(google_place_id)),
                    google_rating = COALESCE(VALUES(google_rating), google_rating),
                    google_rating_count = COALESCE(VALUES(google_rating_count), google_rating_count),
                    last_seen_at = VALUES(last_seen_at)
                """;

            var parameters = new
            {
                existingId,
                businessStatus = place.BusinessStatus is "operational" or "temporarily_closed" or "permanently_closed" ? place.BusinessStatus : null,
                phone = Truncate(place.Phone, 40),
                instagram = SafeLink(place.Instagram),
                facebook = SafeLink(place.Facebook),
                tiktok = SafeLink(place.TikTok),
                youtube = SafeLink(place.YouTube),
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

            var affected = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));

            // LAST_INSERT_ID(id) no UPSERT devolve o id do registro atualizado.
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: ct));

            await transaction.CommitAsync(ct);
            return (id, existingId is null && affected == 1);
        }
        finally
        {
            await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT RELEASE_LOCK('belrestaurante:ingestion')", cancellationToken: CancellationToken.None));
        }
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
            return "\0impossivel";
        }

        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var take = Math.Min(tokens.Length, 8);
        return string.Join(' ', tokens[..take]);
    }

    private static string? SafeLink(string? value) =>
        SocialLinkRules.TryValidate(value, out var link) ? Truncate(link, 350) : null;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private sealed class CandidateRow
    {
        public long Id { get; set; } = default!;
        public string NormalizedName { get; set; } = default!;
        public string NormalizedCity { get; set; } = default!;
        public string? GooglePlaceId { get; set; } = default!;
    }

    private sealed class ObservationRow
    {
        public long Id { get; set; } = default!;
        public long RestaurantId { get; set; } = default!;
        public string Provider { get; set; } = default!;
        public string ExternalId { get; set; } = default!;
        public string? SourceUrl { get; set; } = default!;
        public DateTimeOffset FirstSeenAt { get; set; } = default!;
        public DateTimeOffset LastSeenAt { get; set; } = default!;
        public string DataQuality { get; set; } = default!;
        public string? PayloadHash { get; set; } = default!;
    }
}