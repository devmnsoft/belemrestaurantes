namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Leitura de indicadores reais para o painel administrativo.</summary>
public interface IAdminDashboardRepository
{
    Task<AdminDashboardSnapshot> GetSnapshotAsync(CancellationToken ct);
}

public sealed record AdminDashboardSnapshot(
    int PendingRestaurants,
    int PublishedRestaurants,
    int TotalRestaurants,
    int PendingSuggestions,
    int DeadLetteredJobs,
    int AdminUsers,
    DateTimeOffset? LastDiscoveryCycleAt,
    decimal AiCostMonthToDateUsd,
    IReadOnlyList<AdminCycleSummary> RecentCycles);

public sealed record AdminCycleSummary(
    DateTimeOffset StartedAt,
    int JobsClaimed,
    int PlacesDiscovered,
    int RestaurantsCreated,
    int RestaurantsUpdated,
    int JobsFailed,
    TimeSpan? Duration);

/// <summary>Trilha de auditoria (somente leitura, para o painel).</summary>
public interface IAuditReader
{
    Task<IReadOnlyList<AuditEventView>> ListRecentAsync(int limit, CancellationToken ct);
}

public sealed record AuditEventView(
    long Id,
    string? Actor,
    string Action,
    string EntityType,
    long? EntityId,
    DateTimeOffset OccurredAt,
    string? Metadata);
