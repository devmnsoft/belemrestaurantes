using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlDiscoveryTelemetryRepository(MySqlConnectionFactory connectionFactory)
    : IDiscoveryTelemetryRepository
{
    public async Task RecordTelemetryAsync(
        string serviceName,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string sourceQueried,
        int recordsCreated,
        int recordsUpdated,
        int apiUnitsConsumed,
        string status,
        string? errorMessage,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);

        const string sql = @"
INSERT INTO discovery_run_telemetry
(service_name, started_at, finished_at, duration_ms, source_queried, records_created, records_updated, api_units_consumed, status, error_message, created_at)
VALUES
(@ServiceName, @StartedAt, @FinishedAt, @DurationMs, @SourceQueried, @RecordsCreated, @RecordsUpdated, @ApiUnitsConsumed, @Status, @ErrorMessage, @CreatedAt);";

        var durationMs = (int)(finishedAt - startedAt).TotalMilliseconds;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            ServiceName = serviceName,
            StartedAt = startedAt.UtcDateTime,
            FinishedAt = finishedAt.UtcDateTime,
            DurationMs = Math.Max(0, durationMs),
            SourceQueried = sourceQueried,
            RecordsCreated = recordsCreated,
            RecordsUpdated = recordsUpdated,
            ApiUnitsConsumed = apiUnitsConsumed,
            Status = status,
            ErrorMessage = errorMessage,
            CreatedAt = DateTimeOffset.UtcNow.UtcDateTime
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DiscoveryTelemetryRecord>> ListRecentTelemetryAsync(int limit = 25, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);

        const string sql = @"
SELECT
  id AS Id,
  service_name AS ServiceName,
  started_at AS StartedAt,
  finished_at AS FinishedAt,
  duration_ms AS DurationMs,
  source_queried AS SourceQueried,
  records_created AS RecordsCreated,
  records_updated AS RecordsUpdated,
  api_units_consumed AS ApiUnitsConsumed,
  status AS Status,
  error_message AS ErrorMessage,
  created_at AS CreatedAt
FROM discovery_run_telemetry
ORDER BY id DESC
LIMIT @Limit;";

        var rows = await conn.QueryAsync<DiscoveryTelemetryRecord>(new CommandDefinition(sql, new { Limit = Math.Clamp(limit, 1, 100) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<DiscoveryTelemetrySummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);

        const string sqlLast = @"
SELECT
  service_name AS ServiceName,
  source_queried AS SourceQueried,
  duration_ms AS DurationMs,
  finished_at AS FinishedAt
FROM discovery_run_telemetry
ORDER BY id DESC
LIMIT 1;";

        const string sqlStats = @"
SELECT
  COUNT(*) AS TotalCyclesToday,
  COALESCE(SUM(records_created), 0) AS TotalRecordsDiscoveredToday,
  COALESCE(SUM(records_updated), 0) AS TotalRecordsUpdatedToday,
  COALESCE(SUM(api_units_consumed), 0) AS TotalApiUnitsToday,
  COALESCE(SUM(CASE WHEN status = 'error' THEN 1 ELSE 0 END), 0) AS FailedCyclesToday
FROM discovery_run_telemetry
WHERE started_at >= CURRENT_DATE();";

        var last = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(sqlLast, cancellationToken: ct));
        var stats = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(sqlStats, cancellationToken: ct));

        var summary = new DiscoveryTelemetrySummary();

        if (last != null)
        {
            summary.LastExecutionAt = last.FinishedAt != null ? (DateTimeOffset)last.FinishedAt : null;
            summary.LastService = (string)(last.ServiceName ?? "Crawler & Intelligence");
            summary.LastSourceQueried = (string)(last.SourceQueried ?? "Sitemaps & Schema.org");
            summary.LastDurationMs = (int)(last.DurationMs ?? 0);
        }

        if (stats != null)
        {
            summary.TotalCyclesToday = (int)(stats.TotalCyclesToday ?? 0);
            summary.TotalRecordsDiscoveredToday = (int)(stats.TotalRecordsDiscoveredToday ?? 0);
            summary.TotalRecordsUpdatedToday = (int)(stats.TotalRecordsUpdatedToday ?? 0);
            summary.TotalApiUnitsToday = (int)(stats.TotalApiUnitsToday ?? 0);
            summary.FailedCyclesToday = (int)(stats.FailedCyclesToday ?? 0);
        }

        return summary;
    }
}
