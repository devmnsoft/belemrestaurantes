using Microsoft.Extensions.Configuration;
using Dapper;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Fila persistente de descoberta. O resgate de jobs usa
/// <c>FOR UPDATE SKIP LOCKED</c>: duas instâncias do Windows Service nunca
/// processam o mesmo job, mesmo started ao mesmo tempo.
/// </summary>
public sealed class MySqlDiscoveryQueue(MySqlConnectionFactory factory, IClock clock) : IDiscoveryQueue
{
    public async Task<int> RetryDeadLettersAsync(string city, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE discovery_jobs j JOIN discovery_targets t ON t.id=j.target_id
            SET j.state='pending',j.attempts=0,j.next_attempt_at=@now,j.last_error=NULL,
                j.locked_until=NULL,j.locked_by=NULL,j.completed_at=NULL
            WHERE t.normalized_city=@city AND t.enabled=TRUE AND j.state IN ('dead_lettered','failed')
            """, new { city = NameNormalizer.Normalize(city), now = clock.UtcNow }, cancellationToken: ct));
    }
    public async Task<IReadOnlyList<DiscoveryWorkItem>> ClaimDueAsync(int batchSize, string instanceId, TimeSpan lockDuration, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await ClaimOnceAsync(batchSize, instanceId, lockDuration, ct); }
            catch (MySqlConnector.MySqlException ex) when (attempt < 2 && ex.Number is 1213 or 1205)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1) + Random.Shared.Next(50)), ct);
            }
        }
    }

    private async Task<IReadOnlyList<DiscoveryWorkItem>> ClaimOnceAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 1, 100);
        var now = clock.UtcNow;
        var lockedUntil = now.Add(lockDuration);

        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

        var rows = (await connection.QueryAsync<JobRow>(new CommandDefinition(
            """
            SELECT j.id, j.target_id, j.city, j.category, j.attempts
            FROM discovery_jobs j JOIN discovery_targets t ON t.id = j.target_id
            WHERE j.state = 'pending' AND j.next_attempt_at <= @now AND t.enabled = TRUE
            ORDER BY t.priority DESC,j.next_attempt_at, j.id
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
            """,
            new { now, batchSize },
            transaction,
            cancellationToken: ct))).AsList();

        if (rows.Count > 0)
        {
            // Uma única instrução marca o lote como retido. O WHERE state='pending'
            // impede que um job já tomado por outra instância seja roubado.
            var ids = rows.Select(r => r.id).ToArray();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE discovery_jobs
                SET state = 'running', locked_until = @lockedUntil, locked_by = @instanceId
                WHERE id IN @ids AND state = 'pending'
                """,
                new { ids, lockedUntil, instanceId },
                transaction,
                cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);

        return rows
            .Select(r => new DiscoveryWorkItem(r.id, r.target_id, r.city, r.category, r.attempts))
            .ToArray();
    }

    public Task CompleteAsync(long jobId,CancellationToken ct)=>CompleteCoreAsync(jobId,null,ct);
    public Task CompletePageAsync(long jobId,DiscoveryPageProgress progress,CancellationToken ct)=>CompleteCoreAsync(jobId,progress,ct);
    private async Task CompleteCoreAsync(long jobId,DiscoveryPageProgress? progress,CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_targets t JOIN discovery_jobs j ON j.target_id = t.id
            SET t.next_run_at = TIMESTAMPADD(MINUTE,CASE WHEN COALESCE(@more,t.result_more) THEN 5 ELSE t.refresh_minutes END,@now),
                t.result_offset=COALESCE(@offset,t.result_offset),t.result_more=COALESCE(@more,t.result_more),
                t.result_snapshot_at=COALESCE(@snapshot,t.result_snapshot_at),t.result_source_total=COALESCE(@sourceTotal,t.result_source_total), t.last_success_at = @now, t.locked_until = NULL, t.last_error = NULL
            WHERE j.id = @jobId AND j.state = 'running'
            """, new { jobId, now = clock.UtcNow,offset=progress?.NextOffset,more=progress?.HasMore,snapshot=progress?.SnapshotAt,sourceTotal=progress?.SourceTotal },
            transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_jobs
            SET state = 'succeeded', completed_at = @now, locked_until = NULL, locked_by = NULL, last_error = NULL
            WHERE id = @jobId
            """,
            new { jobId, now = clock.UtcNow },
            transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    public async Task FailAsync(long jobId, string error, TimeSpan backoff, int maxAttempts, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_jobs
            SET state = CASE WHEN attempts + 1 >= @maxAttempts THEN 'dead_lettered' ELSE 'pending' END,
                next_attempt_at = CASE WHEN attempts + 1 >= @maxAttempts THEN @now ELSE @nextAttempt END,
                attempts = attempts + 1,
                locked_until = NULL,
                locked_by = NULL,
                last_error = @error
            WHERE id = @jobId
            """,
            new
            {
                jobId,
                maxAttempts,
                now,
                nextAttempt = now.Add(backoff),
                error = error.Length > 500 ? error[..500] : error
            },
            cancellationToken: ct));
    }

    public async Task DeferAsync(long jobId, string error, DateTimeOffset retryAt, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE discovery_jobs
            SET state = 'pending', next_attempt_at = @retryAt,
                locked_until = NULL, locked_by = NULL, last_error = @error
            WHERE id = @jobId AND state = 'running'
            """, new { jobId, retryAt, error = error.Length > 500 ? error[..500] : error }, cancellationToken: ct));
    }

    public async Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        // Jobs presos em 'running' além do prazo pertencem a um worker que caiu.
        // Devolvê-los é o que evita que a fila fique parada para sempre.
        var released = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_jobs
            SET state = 'pending', locked_until = NULL, locked_by = NULL,
                last_error = CONCAT('lock expirado em ', locked_by)
            WHERE state = 'running' AND (locked_until IS NULL OR locked_until <= @now)
            """,
            new { now },
            cancellationToken: ct));

        return released;
    }

    public async Task<int> EnqueueDueTargetsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var acquired = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT GET_LOCK('belrestaurante:enqueue', 0)", cancellationToken: ct));
        if (acquired != 1) return 0;
        try
        {
        // Um job por alvo vencido, e apenas se não houver job aberto para ele.
        var created = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO discovery_jobs (target_id, city, category, state, next_attempt_at, created_at)
            SELECT t.id, t.city, t.category, 'pending', @now, @now
            FROM discovery_targets t
            WHERE t.enabled = TRUE
              AND t.next_run_at <= @now
              AND (t.locked_until IS NULL OR t.locked_until <= @now)
              AND NOT EXISTS (
                  SELECT 1 FROM discovery_jobs j
                  WHERE j.target_id = t.id AND j.state IN ('pending','running','dead_lettered')
              )
            """,
            new { now },
            cancellationToken: ct));

        if (created > 0)
        {
            // O alvo fica retido até o próximo ciclo; evita recriar job a cada tique.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE discovery_targets t
                JOIN discovery_jobs j ON j.target_id = t.id
                SET t.locked_until = @now, t.attempts = t.attempts + 1
                WHERE (t.locked_until IS NULL OR t.locked_until <= @now)
                  AND j.state = 'pending'
                """,
                new { now },
                cancellationToken: ct));
        }

        return created;
        }
        finally
        {
            await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT RELEASE_LOCK('belrestaurante:enqueue')", cancellationToken: CancellationToken.None));
        }
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM discovery_jobs WHERE state IN ('pending','running')", cancellationToken: ct));
    }

    public async Task<int> CountDeadLetteredAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM discovery_jobs WHERE state = 'dead_lettered'", cancellationToken: ct));
    }

    private sealed class JobRow
    {
        public long id { get; set; } = default!;
        public long target_id { get; set; } = default!;
        public string city { get; set; } = default!;
        public string category { get; set; } = default!;
        public int attempts { get; set; } = default!;
    }
}
