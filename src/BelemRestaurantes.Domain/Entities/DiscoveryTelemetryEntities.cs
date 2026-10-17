using System;

namespace BelemRestaurantes.Domain.Entities;

public sealed class DiscoveryTelemetryRecord
{
    public long Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public int DurationMs { get; set; }
    public string SourceQueried { get; set; } = string.Empty;
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int ApiUnitsConsumed { get; set; }
    public string Status { get; set; } = "success"; // success, warning, error
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DiscoveryTelemetrySummary
{
    public DateTimeOffset? LastExecutionAt { get; set; }
    public string LastService { get; set; } = "Crawler & Intelligence";
    public string LastSourceQueried { get; set; } = "Sitemaps & Schema.org";
    public int LastDurationMs { get; set; }
    public int TotalCyclesToday { get; set; }
    public int TotalRecordsDiscoveredToday { get; set; }
    public int TotalRecordsUpdatedToday { get; set; }
    public int TotalApiUnitsToday { get; set; }
    public int FailedCyclesToday { get; set; }
}
