using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class MySqlCrawlerTelemetryRepository : ICrawlerTelemetryRepository
{
    private readonly MySqlConnectionFactory _factory;

    public MySqlCrawlerTelemetryRepository(MySqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task RecordRunAsync(CrawlerRunLog run, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            INSERT INTO crawler_runs (source_id, url, status_code, duration_ms, items_discovered, items_ingested, error_message)
            VALUES (@SourceId, @Url, @StatusCode, @DurationMs, @ItemsDiscovered, @ItemsIngested, @ErrorMessage);
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                run.SourceId,
                run.Url,
                run.StatusCode,
                run.DurationMs,
                run.ItemsDiscovered,
                run.ItemsIngested,
                run.ErrorMessage
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CrawlerRunLog>> ListRecentRunsAsync(int limit, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT id, source_id AS SourceId, url, status_code AS StatusCode, duration_ms AS DurationMs,
                   items_discovered AS ItemsDiscovered, items_ingested AS ItemsIngested,
                   error_message AS ErrorMessage, created_at AS CreatedAt
            FROM crawler_runs
            ORDER BY id DESC
            LIMIT @Limit;
            """;

        var list = await connection.QueryAsync<CrawlerRunLog>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<CrawlerSummaryStats> GetSummaryStatsAsync(CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT 
              (SELECT COUNT(*) FROM crawler_sources) AS TotalSources,
              (SELECT COUNT(*) FROM crawler_sources WHERE status = 'active') AS ActiveSources,
              (SELECT COUNT(*) FROM crawler_sources WHERE status = 'blocked') AS BlockedSources,
              (SELECT COUNT(*) FROM crawler_tasks WHERE state = 'pending') AS TotalTasksPending,
              (SELECT COUNT(*) FROM crawler_runs WHERE created_at >= CURDATE()) AS TotalRunsToday,
              (SELECT COALESCE(SUM(items_discovered), 0) FROM crawler_runs WHERE created_at >= CURDATE()) AS TotalDiscoveredToday,
              (SELECT COALESCE(SUM(items_ingested), 0) FROM crawler_runs WHERE created_at >= CURDATE()) AS TotalIngestedToday,
              (SELECT COUNT(*) FROM crawler_runs WHERE created_at >= CURDATE() AND error_message IS NOT NULL) AS TotalErrorsToday;
            """;

        return await connection.QueryFirstOrDefaultAsync<CrawlerSummaryStats>(new CommandDefinition(sql, cancellationToken: ct))
               ?? new CrawlerSummaryStats(0, 0, 0, 0, 0, 0, 0, 0);
    }
}
