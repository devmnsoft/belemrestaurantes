using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlDistrictLocalityRepository(MySqlConnectionFactory factory) : IGeoDistrictLocalityRepository
{
    public async Task<IReadOnlyList<GeoDistrictLocality>> ListByMunicipalityAsync(uint municipalityIbge, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   municipality_ibge AS MunicipalityIbge,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   locality_type AS LocalityType,
                   is_tourist_highlight AS IsTouristHighlight,
                   created_at AS CreatedAt
            FROM geo_districts_localities
            WHERE municipality_ibge = @municipalityIbge
            ORDER BY is_tourist_highlight DESC, name ASC;
            """;

        var list = await connection.QueryAsync<GeoDistrictLocality>(
            new CommandDefinition(sql, new { municipalityIbge }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<IReadOnlyList<GeoDistrictLocality>> ListTouristHighlightsAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   municipality_ibge AS MunicipalityIbge,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   locality_type AS LocalityType,
                   is_tourist_highlight AS IsTouristHighlight,
                   created_at AS CreatedAt
            FROM geo_districts_localities
            WHERE is_tourist_highlight = TRUE
            ORDER BY municipality_ibge ASC, name ASC;
            """;

        var list = await connection.QueryAsync<GeoDistrictLocality>(new CommandDefinition(sql, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<GeoDistrictLocality?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   municipality_ibge AS MunicipalityIbge,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   locality_type AS LocalityType,
                   is_tourist_highlight AS IsTouristHighlight,
                   created_at AS CreatedAt
            FROM geo_districts_localities
            WHERE id = @id
            LIMIT 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<GeoDistrictLocality>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<GeoDistrictLocality?> GetByNameAsync(string name, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   municipality_ibge AS MunicipalityIbge,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   locality_type AS LocalityType,
                   is_tourist_highlight AS IsTouristHighlight,
                   created_at AS CreatedAt
            FROM geo_districts_localities
            WHERE LOWER(name) = LOWER(@name) OR LOWER(normalized_name) = LOWER(@name)
            LIMIT 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<GeoDistrictLocality>(
            new CommandDefinition(sql, new { name = name.Trim() }, cancellationToken: ct));
    }
}

public sealed class MySqlDiscoveryIntelligenceQueue(MySqlConnectionFactory factory) : IDiscoveryIntelligenceQueue
{
    private static string MapTaskType(DiscoveryTaskType taskType) => taskType switch
    {
        DiscoveryTaskType.NewDiscovery => "new_discovery",
        DiscoveryTaskType.UpdateExisting => "update_existing",
        DiscoveryTaskType.Enrichment => "enrichment",
        DiscoveryTaskType.SourceVerification => "source_verification",
        DiscoveryTaskType.Deduplication => "deduplication",
        DiscoveryTaskType.ClosureValidation => "closure_validation",
        DiscoveryTaskType.MediaReview => "media_review",
        DiscoveryTaskType.ConflictResolution => "conflict_resolution",
        _ => "new_discovery"
    };

    public async Task<long> EnqueueJobAsync(
        DiscoveryTaskType taskType,
        long? restaurantId,
        string sourceProvider,
        int priority,
        string? payloadJson,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var typeStr = MapTaskType(taskType);

        const string sql = """
            INSERT INTO discovery_intelligence_jobs
              (task_type, restaurant_id, source_provider, priority, status, payload_json, attempts, next_attempt_at, created_at)
            VALUES
              (@typeStr, @restaurantId, @sourceProvider, @priority, 'pending', @payloadJson, 0, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new { typeStr, restaurantId, sourceProvider, priority, payloadJson }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DiscoveryIntelligenceJob>> ClaimDueJobsAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var lockedUntil = DateTimeOffset.UtcNow.Add(lockDuration).UtcDateTime;

        const string updateSql = """
            UPDATE discovery_intelligence_jobs
            SET status = 'running',
                locked_until = @lockedUntil,
                locked_by = @instanceId
            WHERE status = 'pending'
              AND next_attempt_at <= CURRENT_TIMESTAMP(6)
              AND (locked_until IS NULL OR locked_until < CURRENT_TIMESTAMP(6))
            ORDER BY priority DESC, next_attempt_at ASC
            LIMIT @batchSize;
            """;

        await connection.ExecuteAsync(new CommandDefinition(updateSql, new { lockedUntil, instanceId, batchSize }, cancellationToken: ct));

        const string selectSql = """
            SELECT id AS Id,
                   task_type AS TaskType,
                   restaurant_id AS RestaurantId,
                   source_provider AS SourceProvider,
                   priority AS Priority,
                   status AS Status,
                   payload_json AS PayloadJson,
                   attempts AS Attempts,
                   max_attempts AS MaxAttempts,
                   next_attempt_at AS NextAttemptAt,
                   locked_until AS LockedUntil,
                   locked_by AS LockedBy,
                   last_error AS LastError,
                   created_at AS CreatedAt,
                   completed_at AS CompletedAt
            FROM discovery_intelligence_jobs
            WHERE locked_by = @instanceId AND status = 'running'
            LIMIT @batchSize;
            """;

        var list = await connection.QueryAsync<DiscoveryIntelligenceJob>(
            new CommandDefinition(selectSql, new { instanceId, batchSize }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task CompleteJobAsync(long jobId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE discovery_intelligence_jobs
            SET status = 'completed',
                locked_until = NULL,
                locked_by = NULL,
                completed_at = CURRENT_TIMESTAMP(6)
            WHERE id = @jobId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    public async Task FailJobAsync(long jobId, string error, TimeSpan backoff, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var nextAttempt = DateTimeOffset.UtcNow.Add(backoff).UtcDateTime;
        const string sql = """
            UPDATE discovery_intelligence_jobs
            SET status = CASE WHEN attempts >= max_attempts THEN 'dead_letter' ELSE 'pending' END,
                attempts = attempts + 1,
                last_error = @error,
                next_attempt_at = @nextAttempt,
                locked_until = NULL,
                locked_by = NULL
            WHERE id = @jobId;
            """;

        var safeError = error.Length > 490 ? error[..490] : error;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { jobId, error = safeError, nextAttempt }, cancellationToken: ct));
    }

    public async Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE discovery_intelligence_jobs
            SET status = 'pending',
                locked_until = NULL,
                locked_by = NULL
            WHERE status = 'running' AND locked_until < @now;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(sql, new { now = now.UtcDateTime }, cancellationToken: ct));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetJobCountsByStatusAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = "SELECT status, COUNT(*) as cnt FROM discovery_intelligence_jobs GROUP BY status;";
        var rows = await connection.QueryAsync<(string status, int cnt)>(new CommandDefinition(sql, cancellationToken: ct));
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            dict[r.status] = r.cnt;
        }
        return dict;
    }
}

public sealed class MySqlCommercialMonetizationRepository(MySqlConnectionFactory factory) : ICommercialMonetizationRepository
{
    public async Task<CommercialInvoice> CreateInvoiceAsync(
        long restaurantId,
        long? claimId,
        string planCode,
        uint amountCents,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var txid = $"BLM{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomNumberGenerator.GetInt32(1000, 9999)}";
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);

        // Chave PIX oficial CNPJ MNSOFT
        const string pixCnpj = "18160057000113";
        var amountStr = (amountCents / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var pixPayload = $"00020126360014br.gov.bcb.pix0114{pixCnpj}52040000530398654{amountStr.Length:D2}{amountStr}5802BR5916MNSOFT SOLUCOES6005BELEM62070503{txid}6304ABCD";

        // Gerar hash de autenticação interna para confirmação segura (anti-forgery / anti-fraude)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pixCnpj));
        var rawHash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{txid}:{amountCents}:{restaurantId}"));
        var authHash = Convert.ToHexString(rawHash);

        const string sql = """
            INSERT INTO commercial_invoices
              (restaurant_id, claim_id, plan_code, amount_cents, pix_txid, pix_copy_paste, pix_status, auth_hash, expires_at, created_at)
            VALUES
              (@restaurantId, @claimId, @planCode, @amountCents, @txid, @pixPayload, 'pending', @authHash, @expiresAt, CURRENT_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            restaurantId,
            claimId,
            planCode,
            amountCents,
            txid,
            pixPayload,
            authHash,
            expiresAt = expiresAt.UtcDateTime
        }, cancellationToken: ct));

        return new CommercialInvoice
        {
            Id = id,
            RestaurantId = restaurantId,
            ClaimId = claimId,
            PlanCode = planCode,
            AmountCents = amountCents,
            PixTxid = txid,
            PixCopyPaste = pixPayload,
            PixStatus = "pending",
            AuthHash = authHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<CommercialInvoice?> GetByTxidAsync(string pixTxid, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   claim_id AS ClaimId,
                   plan_code AS PlanCode,
                   amount_cents AS AmountCents,
                   pix_txid AS PixTxid,
                   pix_copy_paste AS PixCopyPaste,
                   pix_status AS PixStatus,
                   auth_hash AS AuthHash,
                   paid_at AS PaidAt,
                   expires_at AS ExpiresAt,
                   created_at AS CreatedAt
            FROM commercial_invoices
            WHERE pix_txid = @pixTxid
            LIMIT 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<CommercialInvoice>(
            new CommandDefinition(sql, new { pixTxid }, cancellationToken: ct));
    }

    public async Task<bool> ConfirmPaymentAsync(string pixTxid, string authHash, DateTimeOffset paidAt, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE commercial_invoices
            SET pix_status = 'paid',
                paid_at = @paidAt
            WHERE pix_txid = @pixTxid
              AND auth_hash = @authHash
              AND pix_status = 'pending';
            """;

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            pixTxid,
            authHash,
            paidAt = paidAt.UtcDateTime
        }, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<IReadOnlyList<CommercialInvoice>> ListInvoicesByRestaurantAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   claim_id AS ClaimId,
                   plan_code AS PlanCode,
                   amount_cents AS AmountCents,
                   pix_txid AS PixTxid,
                   pix_copy_paste AS PixCopyPaste,
                   pix_status AS PixStatus,
                   auth_hash AS AuthHash,
                   paid_at AS PaidAt,
                   expires_at AS ExpiresAt,
                   created_at AS CreatedAt
            FROM commercial_invoices
            WHERE restaurant_id = @restaurantId
            ORDER BY created_at DESC;
            """;

        var list = await connection.QueryAsync<CommercialInvoice>(
            new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<IReadOnlyList<CommercialInvoice>> ListRecentInvoicesAsync(int limit, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   restaurant_id AS RestaurantId,
                   claim_id AS ClaimId,
                   plan_code AS PlanCode,
                   amount_cents AS AmountCents,
                   pix_txid AS PixTxid,
                   pix_copy_paste AS PixCopyPaste,
                   pix_status AS PixStatus,
                   auth_hash AS AuthHash,
                   paid_at AS PaidAt,
                   expires_at AS ExpiresAt,
                   created_at AS CreatedAt
            FROM commercial_invoices
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        var list = await connection.QueryAsync<CommercialInvoice>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return list.AsList();
    }
}
