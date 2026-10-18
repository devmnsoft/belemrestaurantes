using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class MySqlCrawlerTaskQueue : ICrawlerTaskQueue
{
    private readonly MySqlConnectionFactory _factory;

    public MySqlCrawlerTaskQueue(MySqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<bool> EnqueueTaskAsync(CrawlerTask task, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            INSERT IGNORE INTO crawler_tasks (source_id, url, city, category, state, depth)
            VALUES (@SourceId, @Url, @City, @Category, 'pending', @Depth);
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { task.SourceId, task.Url, task.City, task.Category, task.Depth },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task<int> EnqueueBatchTasksAsync(IEnumerable<CrawlerTask> tasks, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            INSERT IGNORE INTO crawler_tasks (source_id, url, city, category, state, depth)
            VALUES (@SourceId, @Url, @City, @Category, 'pending', @Depth);
            """;

        var count = await connection.ExecuteAsync(new CommandDefinition(sql, tasks, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return count;
    }

    public async Task<IReadOnlyList<CrawlerTask>> ClaimDueTasksAsync(int batchSize, string instanceId, TimeSpan lockDuration, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var lockedUntil = now.Add(lockDuration);

        const string selectSql = """
            SELECT id, source_id AS SourceId, url, city, category, state, depth, attempts,
                   next_attempt_at AS NextAttemptAt, locked_until AS LockedUntil, locked_by AS LockedBy,
                   last_error AS LastError, created_at AS CreatedAt
            FROM crawler_tasks
            WHERE state = 'pending'
              AND EXISTS(SELECT 1 FROM crawler_sources source WHERE source.id=crawler_tasks.source_id AND source.status='active')
              AND next_attempt_at <= @Now
              AND (locked_until IS NULL OR locked_until <= @Now)
            ORDER BY next_attempt_at ASC, id ASC
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED;
            """;

        var tasks = (await connection.QueryAsync<CrawlerTask>(new CommandDefinition(
            selectSql,
            new { Now = now, BatchSize = batchSize },
            tx,
            cancellationToken: ct))).AsList();

        if (tasks.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return [];
        }

        const string lockSql = """
            UPDATE crawler_tasks
            SET state = 'running',
                locked_until = @LockedUntil,
                locked_by = @LockedBy,
                attempts = attempts + 1
            WHERE id = @Id;
            """;

        foreach (var task in tasks)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                lockSql,
                new { Id = task.Id, LockedUntil = lockedUntil, LockedBy = instanceId },
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return tasks;
    }

    public async Task CompleteTaskAsync(long taskId, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_tasks
            SET state = 'succeeded',
                locked_until = NULL,
                locked_by = NULL,
                completed_at = CURRENT_TIMESTAMP(6)
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = taskId }, cancellationToken: ct));
    }

    public async Task FailTaskAsync(long taskId, string error, TimeSpan backoff, int maxAttempts, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_tasks
            SET state = CASE WHEN attempts >= @MaxAttempts THEN 'failed' ELSE 'pending' END,
                next_attempt_at = @NextAttempt,
                locked_until = NULL,
                locked_by = NULL,
                last_error = @Error
            WHERE id = @Id;
            """;

        var nextAttempt = DateTimeOffset.UtcNow.Add(backoff);

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = taskId, MaxAttempts = maxAttempts, NextAttempt = nextAttempt, Error = error },
            cancellationToken: ct));
    }

    public async Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_tasks
            SET state = 'pending',
                locked_until = NULL,
                locked_by = NULL
            WHERE state = 'running'
              AND locked_until < @Now;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(sql, new { Now = now }, cancellationToken: ct));
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT COUNT(*)
            FROM crawler_tasks
            WHERE state = 'pending';
            """;

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }
}
