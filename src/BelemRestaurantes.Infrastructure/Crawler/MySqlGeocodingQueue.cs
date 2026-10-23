using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;

/// <summary>Fila de geocodificação por ficha, com lease, backoff e reserva diária no banco
/// (compartilhada entre instâncias e sobrevive a reinícios). Mesma proteção Tier 1 do enriquecimento:
/// ficha com vínculo de dono ou reivindicação aprovada nunca é tocada.</summary>
public sealed class MySqlGeocodingQueue(MySqlConnectionFactory factory) : IGeocodingQueue
{
    private const string Eligible = """
        r.latitude IS NULL
        AND CHAR_LENGTH(TRIM(COALESCE(r.address,''))) >= 8
        AND CHAR_LENGTH(TRIM(COALESCE(r.city,''))) >= 2
        AND r.status IN ('pending','published')
        AND NOT EXISTS(SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id=r.id)
        AND NOT EXISTS(SELECT 1 FROM restaurant_claim_requests c WHERE c.restaurant_id=r.id AND c.status='approved')
        """;

    public async Task<int> EnqueueEligibleAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        await using var db = await factory.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO geocoding_tasks(restaurant_id,state,next_run_at,updated_at)
            SELECT r.id,'pending',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6) FROM restaurants r
            LEFT JOIN geocoding_tasks g ON g.restaurant_id=r.id
            WHERE g.restaurant_id IS NULL AND {Eligible}
            ORDER BY r.id LIMIT @limit
            """, new { limit }, cancellationToken: ct));
    }

    public async Task<bool> TryReserveDailyAsync(int dailyLimit, CancellationToken ct)
    {
        dailyLimit = Math.Clamp(dailyLimit, 1, 10000);
        await using var db = await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO geocoding_runs(run_day,requested,granted) VALUES(UTC_DATE(),0,0)", cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("UPDATE geocoding_runs SET requested=requested+1 WHERE run_day=UTC_DATE()", cancellationToken: ct));
        var granted = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_runs SET granted=granted+1 WHERE run_day=UTC_DATE() AND granted<@limit
            """, new { limit = dailyLimit }, cancellationToken: ct));
        return granted > 0;
    }

    public async Task<GeocodingTaskRecord?> ClaimAsync(CancellationToken ct)
    {
        GeocodingTaskRecord? task = null;
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<GeocodingTaskRecord>(new CommandDefinition("""
            SELECT CAST(g.restaurant_id AS SIGNED) AS RestaurantId, r.name AS Name, r.city AS City, r.address AS Address, g.attempts AS Attempts
            FROM geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id
            WHERE ((g.state='pending' AND g.next_run_at<=UTC_TIMESTAMP(6)) OR (g.state='running' AND g.lease_until<UTC_TIMESTAMP(6)))
              AND r.latitude IS NULL
            ORDER BY g.next_run_at,g.restaurant_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, transaction: tx, cancellationToken: ct));
        if (row is not null)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE geocoding_tasks SET state='running',attempts=attempts+1,lease_until=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 3 MINUTE),updated_at=UTC_TIMESTAMP(6)
                WHERE restaurant_id=@RestaurantId
                """, row, tx, cancellationToken: ct));
            task = row;
        }
        await tx.CommitAsync(ct);
        return task;
    }

    public async Task<bool> ApplyCoordinatesAsync(long restaurantId, double latitude, double longitude, string precision, long placeId, string? sourceOverride = null, CancellationToken ct = default)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE restaurants
            SET latitude=@latitude, longitude=@longitude, coordinate_precision=@precision, coordinate_source=@source, updated_at=CURRENT_TIMESTAMP(6)
            WHERE id=@restaurantId AND latitude IS NULL
              AND NOT EXISTS(SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id=@restaurantId)
              AND NOT EXISTS(SELECT 1 FROM restaurant_claim_requests c WHERE c.restaurant_id=@restaurantId AND c.status='approved')
            """, new
            {
                restaurantId,
                latitude,
                longitude,
                precision,
                source = sourceOverride ?? $"OpenStreetMap via Nominatim (objeto {placeId})"
            }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task FinishAsync(long restaurantId, bool terminal, string status, int attempts, string? precision = null, long? placeId = null, CancellationToken ct = default)
    {
        await using var db = await factory.OpenAsync(ct);
        if (terminal)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE geocoding_tasks SET state=@state,last_status=@status,lease_until=NULL,result_precision=@precision,osm_place_id=@placeId,
                next_run_at='2099-12-31 23:59:59',updated_at=UTC_TIMESTAMP(6) WHERE restaurant_id=@restaurantId
                """, new
                {
                    restaurantId,
                    status,
                    precision,
                    placeId,
                    state = status is "rooftop" or "approximate" ? "completed" : "skipped"
                }, cancellationToken: ct));
        }
        else if (attempts >= 3)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE geocoding_tasks SET state='failed',last_status=@status,lease_until=NULL,updated_at=UTC_TIMESTAMP(6) WHERE restaurant_id=@restaurantId
                """, new { restaurantId, status }, cancellationToken: ct));
        }
        else
        {
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE geocoding_tasks SET state='pending',last_status=@status,lease_until=NULL,
                next_run_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL @minutes MINUTE),updated_at=UTC_TIMESTAMP(6) WHERE restaurant_id=@restaurantId
                """, new { restaurantId, status, minutes = (int)Math.Min(Math.Pow(2, Math.Min(attempts, 6)) * 60, 10080) }, cancellationToken: ct));
        }
    }

    public async Task<GeocodingSummary> SummaryAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var counts = await db.QueryFirstAsync<TaskCounts>(new CommandDefinition("""
            SELECT COALESCE(SUM(state='pending'),0) AS Pending, COALESCE(SUM(state='running'),0) AS Running,
                   COALESCE(SUM(state='completed'),0) AS Completed, COALESCE(SUM(state='failed'),0) AS Failed,
                   COALESCE(SUM(state='skipped'),0) AS Skipped FROM geocoding_tasks
            """, cancellationToken: ct));
        var usedToday = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(SUM(granted),0) FROM geocoding_runs WHERE run_day=UTC_DATE()
            """, cancellationToken: ct));
        var eligibleNotQueued = await db.ExecuteScalarAsync<int>(new CommandDefinition($"""
            SELECT COUNT(*) FROM restaurants r LEFT JOIN geocoding_tasks g ON g.restaurant_id=r.id
            WHERE g.restaurant_id IS NULL AND {Eligible}
            """, cancellationToken: ct));
        var publishedWithoutCoordinates = await db.ExecuteScalarAsync<int>(new CommandDefinition($"""
            SELECT COUNT(*) FROM restaurants r WHERE r.status='published' AND {Eligible}
            """, cancellationToken: ct));
        var applied = await db.QueryFirstAsync<AppliedCounts>(new CommandDefinition("""
            SELECT COALESCE(SUM(coordinate_precision='rooftop'),0) AS RooftopApplied,
                   COALESCE(SUM(coordinate_precision='approximate'),0) AS ApproximateApplied
            FROM restaurants WHERE coordinate_source LIKE 'OpenStreetMap via%' OR coordinate_source='Ajuste administrativo (revisão humana)'
            """, cancellationToken: ct));
        var skipRows = (await db.QueryAsync<SkipReasonRow>(new CommandDefinition("""
            SELECT last_status AS Reason, COUNT(*) AS Count FROM geocoding_tasks WHERE state='skipped'
            GROUP BY last_status ORDER BY Count DESC, Reason ASC
            """, cancellationToken: ct))).ToArray();
        var skipReasons = skipRows.ToDictionary(r => r.Reason ?? "(sem motivo)", r => r.Count);
        return new((int)counts.Pending, (int)counts.Running, (int)counts.Completed, (int)counts.Failed, (int)counts.Skipped, usedToday, eligibleNotQueued, publishedWithoutCoordinates, (int)applied.RooftopApplied, (int)applied.ApproximateApplied, skipReasons);
    }

    public async Task<(IReadOnlyList<GeocodingTaskRow> Items, int Total)> ListTasksAsync(string? state, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (state is not (null or "pending" or "running" or "completed" or "failed" or "skipped")) throw new ArgumentException("Estado inválido.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 60);
        var like = string.IsNullOrWhiteSpace(search) ? null : "%" + MySqlRestaurantReadRepository.EscapeLike(search.Trim()) + "%";
        const string where = """
            WHERE (@state IS NULL OR g.state=@state) AND (@like IS NULL OR r.name LIKE @like)
            """;
        await using var db = await factory.OpenAsync(ct);
        var total = await db.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id {where}",
            new { state, like }, cancellationToken: ct));
        var items = (await db.QueryAsync<GeocodingTaskRow>(new CommandDefinition($"""
            SELECT CAST(g.restaurant_id AS SIGNED) AS RestaurantId, r.name AS Name, COALESCE(r.city,'') AS City, g.state AS State, g.attempts AS Attempts,
                   g.last_status AS LastStatus, g.result_precision AS ResultPrecision, CAST(g.osm_place_id AS SIGNED) AS OsmPlaceId,
                   CAST(r.latitude AS DOUBLE) AS Latitude, CAST(r.longitude AS DOUBLE) AS Longitude, g.updated_at AS UpdatedAt, g.next_run_at AS NextRunAt
            FROM geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id {where}
            ORDER BY g.updated_at DESC, g.restaurant_id DESC LIMIT @limit OFFSET @offset
            """, new { state, like, limit = pageSize, offset = (page - 1) * pageSize }, cancellationToken: ct))).ToArray();
        return (items, total);
    }

    public async Task<bool> RequeueAsync(long restaurantId, string actor, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var from = await db.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT state FROM geocoding_tasks WHERE restaurant_id=@restaurantId",
            new { restaurantId }, tx, cancellationToken: ct));
        var n = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id
            SET g.state='pending', g.attempts=0, g.lease_until=NULL, g.next_run_at=UTC_TIMESTAMP(6), g.updated_at=UTC_TIMESTAMP(6)
            WHERE g.restaurant_id=@restaurantId AND g.state IN ('skipped','failed') AND r.latitude IS NULL
            """, new { restaurantId }, tx, cancellationToken: ct));
        if (n > 0) await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at)
            VALUES(@actor,'geocoding.requeue','geocoding_task',@restaurantId,@metadata,UTC_TIMESTAMP(6))
            """, new { actor, restaurantId, metadata = JsonSerializer.Serialize(new { from }) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return n > 0;
    }

    public async Task<int> RequeueStaleTerminalsAsync(string actor, int olderThanDays, CancellationToken ct)
    {
        olderThanDays = Math.Clamp(olderThanDays, 1, 3650);
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var n = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id
            SET g.state='pending', g.attempts=0, g.lease_until=NULL, g.next_run_at=UTC_TIMESTAMP(6), g.updated_at=UTC_TIMESTAMP(6)
            WHERE g.state IN ('skipped','failed') AND r.latitude IS NULL AND g.updated_at <= DATE_SUB(UTC_TIMESTAMP(), INTERVAL @days DAY)
            """, new { days = olderThanDays }, tx, cancellationToken: ct));
        if (n > 0) await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at)
            VALUES(@actor,'geocoding.requeue_stale','geocoding_task',@metadata,UTC_TIMESTAMP(6))
            """, new { actor, metadata = JsonSerializer.Serialize(new { count = n, older_than_days = olderThanDays }) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return n;
    }

    public async Task<int> RequeueByReasonAsync(string reason, string actor, CancellationToken ct)
    {
        if (!Regex.IsMatch(reason ?? "", "^[a-z][a-z0-9_]{0,39}$")) throw new ArgumentException("Motivo de rejeição inválido.");
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var n = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_tasks g JOIN restaurants r ON r.id=g.restaurant_id
            SET g.state='pending', g.attempts=0, g.lease_until=NULL, g.next_run_at=UTC_TIMESTAMP(6), g.updated_at=UTC_TIMESTAMP(6)
            WHERE g.state IN ('skipped','failed') AND g.last_status=@reason AND r.latitude IS NULL
            """, new { reason }, tx, cancellationToken: ct));
        if (n > 0) await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at)
            VALUES(@actor,'geocoding.requeue_by_reason','geocoding_task',@metadata,UTC_TIMESTAMP(6))
            """, new { actor, metadata = JsonSerializer.Serialize(new { reason, count = n }) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return n;
    }

    public async Task<bool> IsStreetRecentlyEmptyAsync(string streetKey, string cityKey, int validDays, CancellationToken ct)
    {
        validDays = Math.Clamp(validDays, 1, 90);
        await using var db = await factory.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM geocoding_street_cache
            WHERE street_key=@streetKey AND city_key=@cityKey AND learned_at > DATE_SUB(NOW(6), INTERVAL @days DAY))
            """, new { streetKey, cityKey, days = validDays }, cancellationToken: ct));
    }

    public async Task RecordStreetEmptyAsync(string streetKey, string cityKey, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_street_cache(street_key,city_key,learned_at) VALUES(@streetKey,@cityKey,NOW(6))
            ON DUPLICATE KEY UPDATE learned_at=NOW(6)
            """, new { streetKey, cityKey }, cancellationToken: ct));
    }

    public async Task ReleaseClaimAsync(long restaurantId, int preClaimAttempts, CancellationToken ct)
    {
        var attempts = Math.Max(preClaimAttempts, 0);
        await using var db = await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_tasks SET state='pending',last_status='daily_limit',lease_until=NULL,
            attempts=GREATEST(attempts-1,0),
            next_run_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL @minutes MINUTE),updated_at=UTC_TIMESTAMP(6) WHERE restaurant_id=@restaurantId
            """, new { restaurantId, minutes = (int)Math.Min(Math.Pow(2, Math.Min(attempts, 6)) * 60, 10080) }, cancellationToken: ct));
    }

    private static readonly JsonSerializerOptions PoolJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Sprint 66 — chave do cache: SHA-256 da consulta exata que o Nominatim recebe (mesmo
    /// <c>BuildQuery</c> da fonte), então rejeitar é sempre byte-fiel à requisição que gerou a resposta.</summary>
    private static string QueryCacheKey(string address, string city) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NominatimGeocodingSource.BuildQuery(address, city)))).ToLowerInvariant();

    public async Task<IReadOnlyList<NominatimCandidate>?> TryGetCachedPoolAsync(string address, string city, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var key = QueryCacheKey(address, city);
        var payload = await db.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT payload_json FROM geocoding_query_cache WHERE query_key=@key AND day=UTC_DATE()
            """, new { key }, cancellationToken: ct));
        if (payload is null) return null;
        var pool = JsonSerializer.Deserialize<List<NominatimCandidate>>(payload, PoolJsonOptions) ?? [];
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_query_cache SET hits=hits+1 WHERE query_key=@key AND day=UTC_DATE()
            """, new { key }, cancellationToken: ct));
        return pool;
    }

    public async Task StoreCachedPoolAsync(string address, string city, IReadOnlyList<NominatimCandidate> pool, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var key = QueryCacheKey(address, city);
        var payload = JsonSerializer.Serialize(pool, PoolJsonOptions);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_query_cache(query_key,day,payload_json,created_at) VALUES(@key,UTC_DATE(),@payload,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE payload_json=VALUES(payload_json),created_at=UTC_TIMESTAMP(6)
            """, new { key, payload }, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM geocoding_query_cache WHERE day < DATE_SUB(UTC_DATE(), INTERVAL 14 DAY)
            """, cancellationToken: ct));
    }

    public async Task<CoordinateRevertResult?> RevertAppliedCoordinatesAsync(long restaurantId, string actor, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var row = await db.QueryFirstOrDefaultAsync<AppliedCoordinate>(new CommandDefinition("""
            SELECT CAST(latitude AS DOUBLE) AS Latitude, CAST(longitude AS DOUBLE) AS Longitude,
                   coordinate_precision AS `Precision`, coordinate_source AS Source
            FROM restaurants WHERE id=@restaurantId
            """, new { restaurantId }, tx, cancellationToken: ct));
        if (row is null || row.Latitude is null || row.Longitude is null || string.IsNullOrWhiteSpace(row.Source)
            || !IsHumanManagedSource(row.Source)) return null;
        var n = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE restaurants
            SET latitude=NULL, longitude=NULL, coordinate_precision=NULL, coordinate_source=NULL, updated_at=CURRENT_TIMESTAMP(6)
            WHERE id=@restaurantId AND latitude IS NOT NULL AND (coordinate_source LIKE 'OpenStreetMap via%' OR coordinate_source=@adminAdjustedSource)
              AND NOT EXISTS(SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id=@restaurantId)
              AND NOT EXISTS(SELECT 1 FROM restaurant_claim_requests c WHERE c.restaurant_id=@restaurantId AND c.status='approved')
            """, new { restaurantId, adminAdjustedSource = AdminAdjustedSource }, tx, cancellationToken: ct));
        if (n == 0) { await tx.RollbackAsync(ct); return null; }
        // Vira rejeição revisada: sem nova tentativa imediata. O operador usa "Re-enfileirar" quando houver o que
        // conferir de novo (endereço corrigido, mapa melhorado); o lote de antigas cobre o resto após 7 dias.
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_tasks(restaurant_id,state,last_status,next_run_at,updated_at)
            VALUES(@restaurantId,'skipped','reverted_by_admin','2099-12-31 23:59:59',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE state='skipped', attempts=0, lease_until=NULL, last_status='reverted_by_admin',
                result_precision=NULL, osm_place_id=NULL, next_run_at='2099-12-31 23:59:59', updated_at=UTC_TIMESTAMP(6)
            """, new { restaurantId }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at)
            VALUES(@actor,'geocoding.revert','geocoding_task',@restaurantId,@metadata,UTC_TIMESTAMP(6))
            """, new { actor, restaurantId, metadata = JsonSerializer.Serialize(new { old = $"{row.Latitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)};{row.Longitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}", source = row.Source }) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new CoordinateRevertResult(row.Latitude.Value, row.Longitude.Value, row.Precision, row.Source);
    }

    public async Task<CoordinateAdjustResult?> AdjustAppliedCoordinatesAsync(long restaurantId, double latitude, double longitude, string precision, string actor, CancellationToken ct)
    {
        if (precision is not ("rooftop" or "approximate")) throw new ArgumentException("Precisão do ajuste inválida: rooftop ou approximate.");
        if (!GeocodingRules.IsCoordinateInRange(latitude, longitude)) throw new ArgumentException("Par fora da caixa de contorno do Pará.");
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var row = await db.QueryFirstOrDefaultAsync<AppliedCoordinate>(new CommandDefinition("""
            SELECT CAST(latitude AS DOUBLE) AS Latitude, CAST(longitude AS DOUBLE) AS Longitude,
                   coordinate_precision AS `Precision`, coordinate_source AS Source
            FROM restaurants WHERE id=@restaurantId
            """, new { restaurantId }, tx, cancellationToken: ct));
        if (row is null || row.Latitude is null || row.Longitude is null || string.IsNullOrWhiteSpace(row.Source)
            || !IsHumanManagedSource(row.Source)) return null;
        var n = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE restaurants
            SET latitude=@lat, longitude=@lng, coordinate_precision=@precision,
                coordinate_source='Ajuste administrativo (revisão humana)', updated_at=CURRENT_TIMESTAMP(6)
            WHERE id=@restaurantId AND latitude IS NOT NULL AND (coordinate_source LIKE 'OpenStreetMap via%' OR coordinate_source=@adminAdjustedSource)
              AND NOT EXISTS(SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id=@restaurantId)
              AND NOT EXISTS(SELECT 1 FROM restaurant_claim_requests c WHERE c.restaurant_id=@restaurantId AND c.status='approved')
            """, new { restaurantId, lat = latitude, lng = longitude, precision, adminAdjustedSource = AdminAdjustedSource }, tx, cancellationToken: ct));
        if (n == 0) { await tx.RollbackAsync(ct); return null; }
        // Revisão finalizada com coordenada: fica 'completed'. O claim exige latitude IS NULL, então nenhuma
        // tentativa automática do Worker sobrescreve o ajuste; os botões Reverter/Ajustar seguem disponíveis.
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_tasks(restaurant_id,state,last_status,result_precision,next_run_at,updated_at)
            VALUES(@restaurantId,'completed','adjusted_by_admin',@precision,'2099-12-31 23:59:59',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE state='completed', attempts=0, lease_until=NULL, last_status='adjusted_by_admin',
                result_precision=@precision, osm_place_id=NULL, next_run_at='2099-12-31 23:59:59', updated_at=UTC_TIMESTAMP(6)
            """, new { restaurantId, precision }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at)
            VALUES(@actor,'geocoding.adjust','geocoding_task',@restaurantId,@metadata,UTC_TIMESTAMP(6))
            """, new { actor, restaurantId, metadata = JsonSerializer.Serialize(new
            {
                old = $"{row.Latitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)};{row.Longitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}",
                value = $"{latitude.ToString("0.0000000", CultureInfo.InvariantCulture)};{longitude.ToString("0.0000000", CultureInfo.InvariantCulture)}",
                precision
            }) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new CoordinateAdjustResult(row.Latitude.Value, row.Longitude.Value, row.Precision, row.Source);
    }

    /// <summary>Origem declarada quando um revisor humano substitui o par aplicado pelo OSM.</summary>
    private const string AdminAdjustedSource = "Ajuste administrativo (revisão humana)";

    /// <summary>Coordenada gerida por este ciclo: aplicada pelo OSM (Nominatim na Sprint 58 ou Overpass na 62)
    /// ou ajustada por revisão humana. Tier 1 e origens externas seguem fora — checagem repetida no pré-guard
    /// C# e no próprio UPDATE.</summary>
    private static bool IsHumanManagedSource(string source) =>
        source.StartsWith("OpenStreetMap via", StringComparison.Ordinal) || source == AdminAdjustedSource;

    private sealed class AppliedCoordinate
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Precision { get; set; }
        public string? Source { get; set; }
        public AppliedCoordinate() { }
    }

    // SUM retorna Decimal e COUNT retorna Int64 no MySQL; o construtor vazio deixa o Dapper converter.
    private sealed class TaskCounts
    {
        public int Pending { get; set; }
        public int Running { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public TaskCounts() { }
    }

    private sealed class AppliedCounts
    {
        public int RooftopApplied { get; set; }
        public int ApproximateApplied { get; set; }
        public AppliedCounts() { }
    }

    // COUNT retorna Int64 no MySQL; o construtor vazio deixa o Dapper converter.
    private sealed class SkipReasonRow
    {
        public string? Reason { get; set; }
        public int Count { get; set; }
        public SkipReasonRow() { }
    }
}
