using Dapper;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>Controle de pausa/reativação por alvo e por cidade.</summary>
public sealed class MySqlDiscoveryTargetRepository(MySqlConnectionFactory factory, IClock clock) : IDiscoveryTargetRepository
{
    public async Task<IReadOnlyList<DiscoveryTarget>> ListAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<TargetRow>(new CommandDefinition(
            """
            SELECT id, city, category, enabled, next_run_at, locked_until,
                   attempts, last_success_at, last_error
            FROM discovery_targets
            ORDER BY city, category
            """,
            cancellationToken: ct));

        return rows.Select(Map).ToArray();
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled, string actor, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await using var connection = await factory.OpenAsync(ct);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_targets
            SET enabled = @enabled,
                next_run_at = CASE WHEN @enabled THEN @now ELSE next_run_at END,
                locked_until = NULL
            WHERE id = @id
            """,
            new { id, enabled, now },
            transaction,
            cancellationToken: ct));

        if (changed > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, metadata, occurred_at)
                VALUES (@actor, @action, 'discovery_target', @id, @metadata, @now)
                """,
                new
                {
                    actor,
                    action = enabled ? "target.enable" : "target.pause",
                    id,
                    metadata = System.Text.Json.JsonSerializer.Serialize(new { enabled, now }),
                    now
                },
                transaction,
                cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return changed > 0;
    }

    public async Task<bool> SetEnabledForCityAsync(string city, bool enabled, string actor, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await using var connection = await factory.OpenAsync(ct);

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_targets
            SET enabled = @enabled,
                next_run_at = CASE WHEN @enabled THEN @now ELSE next_run_at END,
                locked_until = NULL
            WHERE normalized_city = @normalizedCity
            """,
            new { enabled, now, normalizedCity = NameNormalizer.Normalize(city) },
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, metadata, occurred_at)
            VALUES (@actor, @action, 'city', NULL, @metadata, @now)
            """,
            new
            {
                actor,
                action = enabled ? "city.enable_discovery" : "city.pause_discovery",
                metadata = System.Text.Json.JsonSerializer.Serialize(new { city, enabled, now, changed }),
                now
            },
            cancellationToken: ct));

        return changed > 0;
    }

    private static DiscoveryTarget Map(TargetRow row) => new(
        row.id,
        row.city,
        row.category,
        row.enabled,
        row.next_run_at,
        row.locked_until,
        row.attempts,
        row.last_success_at,
        row.last_error);

    private sealed record TargetRow(
        long id, string city, string category, bool enabled, DateTimeOffset next_run_at,
        DateTimeOffset? locked_until, int attempts, DateTimeOffset? last_success_at, string? last_error);
}

/// <summary>Telemetria de execução dos ciclos, consumida pelo dashboard.</summary>
public sealed class MySqlCycleTelemetryRepository(MySqlConnectionFactory factory, IClock clock) : ICycleTelemetryRepository
{
    public async Task<long> StartCycleAsync(string instanceId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO discovery_cycle_runs (instance_id, started_at)
            VALUES (@instanceId, @now);
            SELECT LAST_INSERT_ID();
            """,
            new { instanceId, now },
            cancellationToken: ct));
    }

    public async Task CompleteCycleAsync(
        long cycleId,
        int claimed,
        int discovered,
        int created,
        int updated,
        int failed,
        int skipped,
        TimeSpan duration,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE discovery_cycle_runs
            SET completed_at = @now, duration_ms = @durationMs,
                jobs_claimed = @claimed, places_discovered = @discovered,
                restaurants_created = @created, restaurants_updated = @updated,
                jobs_failed = @failed, places_skipped = @skipped
            WHERE id = @cycleId
            """,
            new
            {
                cycleId, now = clock.UtcNow,
                durationMs = (int)Math.Min(duration.TotalMilliseconds, int.MaxValue),
                claimed, discovered, created, updated, failed, skipped
            },
            cancellationToken: ct));
    }
}