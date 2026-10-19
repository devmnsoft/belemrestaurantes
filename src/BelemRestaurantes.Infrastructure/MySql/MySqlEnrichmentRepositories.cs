using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlEnrichmentQueueRepository(MySqlConnectionFactory factory) : IEnrichmentQueueRepository
{
    public async Task<long> EnqueueAsync(long restaurantId, int priority, string fields, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO restaurant_enrichment_queue
              (restaurant_id, priority, state, fields_to_enrich, attempts, next_attempt_at, created_at)
            VALUES
              (@restaurantId, @priority, 'pending', @fields, 0, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new { restaurantId, priority, fields }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantEnrichmentQueueItem>> ClaimDueEnrichmentsAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var lockedUntil = DateTimeOffset.UtcNow.Add(lockDuration).UtcDateTime;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE restaurant_enrichment_queue SET state=CASE WHEN attempts>=4 THEN 'failed' ELSE 'pending' END,
              attempts=attempts+1,locked_by=NULL,locked_until=NULL,last_error='Lease expirado; processo interrompido',
              next_attempt_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE)
            WHERE state='running' AND locked_until<UTC_TIMESTAMP(6)
            """,cancellationToken:ct));
        await using var transaction=await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,ct);
        var ids=(await connection.QueryAsync<long>(new CommandDefinition("""
            SELECT id FROM restaurant_enrichment_queue WHERE state='pending' AND next_attempt_at<=UTC_TIMESTAMP(6)
            ORDER BY priority DESC,next_attempt_at,id LIMIT @batchSize FOR UPDATE SKIP LOCKED
            """,new{batchSize=Math.Clamp(batchSize,1,100)},transaction,cancellationToken:ct))).ToArray();
        if(ids.Length==0){await transaction.CommitAsync(ct);return [];}

        // Lock atômico por update
        const string updateSql = """
            UPDATE restaurant_enrichment_queue
            SET state = 'running',
                locked_until = @lockedUntil,
                locked_by = @instanceId
            WHERE id IN @ids AND state = 'pending'
              AND next_attempt_at <= UTC_TIMESTAMP(6)
              AND (locked_until IS NULL OR locked_until < UTC_TIMESTAMP(6))
            ORDER BY priority DESC, next_attempt_at ASC
            LIMIT @batchSize;
            """;

        await connection.ExecuteAsync(new CommandDefinition(updateSql, new { ids,lockedUntil, instanceId, batchSize },transaction,cancellationToken: ct));

        const string selectSql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   priority AS Priority,
                   state AS State,
                   fields_to_enrich AS FieldsToEnrich,
                   attempts AS Attempts,
                   next_attempt_at AS NextAttemptAt,
                   locked_until AS LockedUntil,
                   locked_by AS LockedBy,
                   last_error AS LastError,
                   created_at AS CreatedAt,
                   completed_at AS CompletedAt
            FROM restaurant_enrichment_queue
            WHERE id IN @ids AND locked_by = @instanceId AND state = 'running'
            LIMIT @batchSize;
            """;

        var list = await connection.QueryAsync<RestaurantEnrichmentQueueItem>(
            new CommandDefinition(selectSql, new { ids,instanceId, batchSize },transaction,cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return list.AsList();
    }

    public async Task CompleteEnrichmentAsync(long queueId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE restaurant_enrichment_queue
            SET state = 'completed',
                locked_until = NULL,
                locked_by = NULL,
                completed_at = CURRENT_TIMESTAMP(6)
            WHERE id = @queueId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { queueId }, cancellationToken: ct));
    }

    public async Task FailEnrichmentAsync(long queueId, string error, TimeSpan backoff, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var nextAttempt = DateTimeOffset.UtcNow.Add(backoff).UtcDateTime;
        const string sql = """
            UPDATE restaurant_enrichment_queue
            SET state = CASE WHEN attempts >= 4 THEN 'failed' ELSE 'pending' END,
                attempts = attempts + 1,
                last_error = @error,
                next_attempt_at = @nextAttempt,
                locked_until = NULL,
                locked_by = NULL
            WHERE id = @queueId;
            """;

        var safeError = error.Length > 490 ? error[..490] : error;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { queueId, error = safeError, nextAttempt }, cancellationToken: ct));
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = "SELECT COUNT(*) FROM restaurant_enrichment_queue WHERE state = 'pending';";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }
}

public sealed class MySqlRestaurantAttributeHistoryRepository(MySqlConnectionFactory factory) : IRestaurantAttributeHistoryRepository
{
    public async Task RecordHistoryAsync(RestaurantAttributeHistory history, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO restaurant_attribute_history
              (restaurant_id, attribute_name, old_value, new_value, source_type, source_reference, confidence_score, requires_moderation, is_applied, created_at)
            VALUES
              (@RestaurantId, @AttributeName, @OldValue, @NewValue, @SourceType, @SourceReference, @ConfidenceScore, @RequiresModeration, @IsApplied, CURRENT_TIMESTAMP(6));
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, history, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantAttributeHistory>> GetHistoryAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   attribute_name AS AttributeName,
                   old_value AS OldValue,
                   new_value AS NewValue,
                   source_type AS SourceType,
                   source_reference AS SourceReference,
                   confidence_score AS ConfidenceScore,
                   requires_moderation AS RequiresModeration,
                   is_applied AS IsApplied,
                   created_at AS CreatedAt
            FROM restaurant_attribute_history
            WHERE restaurant_id = @restaurantId
            ORDER BY created_at DESC;
            """;

        var list = await connection.QueryAsync<RestaurantAttributeHistory>(
            new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<IReadOnlyList<RestaurantAttributeHistory>> ListRecentChangesAsync(int limit, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   attribute_name AS AttributeName,
                   old_value AS OldValue,
                   new_value AS NewValue,
                   source_type AS SourceType,
                   source_reference AS SourceReference,
                   confidence_score AS ConfidenceScore,
                   requires_moderation AS RequiresModeration,
                   is_applied AS IsApplied,
                   created_at AS CreatedAt
            FROM restaurant_attribute_history
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        var list = await connection.QueryAsync<RestaurantAttributeHistory>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return list.AsList();
    }
}

public sealed class MySqlRestaurantMediaRepository(MySqlConnectionFactory factory) : IRestaurantMediaRepository
{
    public async Task<long> AddMediaAsync(RestaurantMedia media, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO restaurant_media
              (restaurant_id, media_type, media_url, thumbnail_url, embed_html, priority_tier, source_origin, attribution_text, license_type, status, created_at)
            VALUES
              (@RestaurantId, @MediaType, @MediaUrl, @ThumbnailUrl, @EmbedHtml, @PriorityTier, @SourceOrigin, @AttributionText, @LicenseType, @Status, CURRENT_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, media, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantMedia>> GetMediaByRestaurantAsync(long restaurantId, bool onlyApproved = true, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct);
        var sql = onlyApproved
            ? """
              SELECT id AS Id, restaurant_id AS RestaurantId, media_type AS MediaType, media_url AS MediaUrl,
                     thumbnail_url AS ThumbnailUrl, embed_html AS EmbedHtml, priority_tier AS PriorityTier,
                     source_origin AS SourceOrigin, attribution_text AS AttributionText, license_type AS LicenseType,
                     status AS Status, created_at AS CreatedAt
              FROM restaurant_media
              WHERE restaurant_id = @restaurantId AND status = 'approved'
              ORDER BY priority_tier ASC, created_at DESC;
              """
            : """
              SELECT id AS Id, restaurant_id AS RestaurantId, media_type AS MediaType, media_url AS MediaUrl,
                     thumbnail_url AS ThumbnailUrl, embed_html AS EmbedHtml, priority_tier AS PriorityTier,
                     source_origin AS SourceOrigin, attribution_text AS AttributionText, license_type AS LicenseType,
                     status AS Status, created_at AS CreatedAt
              FROM restaurant_media
              WHERE restaurant_id = @restaurantId
              ORDER BY priority_tier ASC, created_at DESC;
              """;

        var list = await connection.QueryAsync<RestaurantMedia>(
            new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<bool> ApproveMediaAsync(long mediaId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = "UPDATE restaurant_media SET status = 'approved' WHERE id = @mediaId;";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { mediaId }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> RejectMediaAsync(long mediaId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = "UPDATE restaurant_media SET status = 'rejected' WHERE id = @mediaId;";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { mediaId }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<IReadOnlyList<RestaurantMedia>> ListPendingModerationAsync(int limit, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id, restaurant_id AS RestaurantId, media_type AS MediaType, media_url AS MediaUrl,
                   thumbnail_url AS ThumbnailUrl, embed_html AS EmbedHtml, priority_tier AS PriorityTier,
                   source_origin AS SourceOrigin, attribution_text AS AttributionText, license_type AS LicenseType,
                   status AS Status, created_at AS CreatedAt
            FROM restaurant_media
            WHERE status = 'pending'
            ORDER BY priority_tier ASC, created_at ASC
            LIMIT @limit;
            """;

        var list = await connection.QueryAsync<RestaurantMedia>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return list.AsList();
    }
}
