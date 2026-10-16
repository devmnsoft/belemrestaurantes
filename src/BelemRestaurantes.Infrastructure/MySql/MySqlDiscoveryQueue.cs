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
    public async Task<IReadOnlyList<DiscoveryWorkItem>> ClaimDueAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 1, 100);
        var now = clock.UtcNow;
        var lockedUntil = now.Add(lockDuration);

        await using var connection = await factory.OpenAsync(ct);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var rows = (await connection.QueryAsync<JobRow>(new CommandDefinition(
            """
            SELECT id, target_id, city, category, attempts
            FROM discovery_jobs
            WHERE state = 'pending' AND next_attempt_at <= @now
            ORDER BY next_attempt_at, id
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

    public async Task CompleteAsync(long jobId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_jobs
            SET state = 'succeeded', completed_at = @now, locked_until = NULL, locked_by = NULL, last_error = NULL
            WHERE id = @jobId
            """,
            new { jobId, now = clock.UtcNow },
            cancellationToken: ct));
    }

    public async Task FailAsync(long jobId, string error, TimeSpan backoff, int maxAttempts, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_jobs
            SET attempts = attempts + 1,
                state = CASE WHEN attempts + 1 >= @maxAttempts THEN 'dead_lettered' ELSE 'pending' END,
                next_attempt_at = CASE WHEN attempts + 1 >= @maxAttempts THEN @now ELSE @nextAttempt END END,
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
                  WHERE j.target_id = t.id AND j.state IN ('pending','running')
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
                WHERE t.locked_until IS NULL OR t.locked_until <= @now
                  AND j.state = 'pending'
                """,
                new { now },
                cancellationToken: ct));
        }

        return created;
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

    private sealed record JobRow(long id, long target_id, string city, string category, int attempts);
}