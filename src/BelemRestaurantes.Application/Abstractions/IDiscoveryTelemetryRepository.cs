using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public interface IDiscoveryTelemetryRepository
{
    Task RecordTelemetryAsync(
        string serviceName,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string sourceQueried,
        int recordsCreated,
        int recordsUpdated,
        int apiUnitsConsumed,
        string status,
        string? errorMessage,
        CancellationToken ct = default);

    Task<IReadOnlyList<DiscoveryTelemetryRecord>> ListRecentTelemetryAsync(int limit = 25, CancellationToken ct = default);

    Task<DiscoveryTelemetrySummary> GetSummaryAsync(CancellationToken ct = default);
}
