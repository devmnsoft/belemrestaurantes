namespace BelemRestaurantes.Application.Models;

/// <summary>Tarefa executada por um ciclo do worker.</summary>
public sealed record DiscoveryWorkItem(
    long JobId,
    long TargetId,
    string City,
    string Category,
    int Attempt);

/// <summary>Contadores de um ciclo, espelhando o que o painel exibe.</summary>
public sealed record DiscoveryCycleMetrics(
    int JobsClaimed,
    int PlacesDiscovered,
    int RestaurantsCreated,
    int RestaurantsUpdated,
    int JobsFailed,
    int PlacesSkipped,
    TimeSpan Duration)
{
    public string Source { get; init; } = "google_places";
    public string? FailureReason { get; init; }
    public DateTimeOffset? ProviderRetryAt { get; init; }
    public static DiscoveryCycleMetrics Empty => new(0, 0, 0, 0, 0, 0, TimeSpan.Zero);

    public override string ToString() =>
        $"descobertos={PlacesDiscovered} novos={RestaurantsCreated} atualizados={RestaurantsUpdated} " +
        $"ignorados={PlacesSkipped} falhas={JobsFailed} duracao={Duration.TotalSeconds:F1}s";
}

/// <summary>Decisão do orquestrador para um lugar recém-descoberto.</summary>
public enum IngestionDecision
{
    /// <summary>Registro novo criado em <c>pending</c>.</summary>
    CreatedPending = 0,

    /// <summary>Registro existente atualizado; segue em <c>pending</c>.</summary>
    UpdatedPending = 1,

    /// <summary>Mesmo lugar já conhecido (mesmo Place ID).</summary>
    Refreshed = 2,

    /// <summary>Descartado por violar regra de dados/licença da fonte.</summary>
    Rejected = 3
}

/// <summary>Resultado de ingerir um lugar descoberta.</summary>
public sealed record IngestionResult(
    IngestionDecision Decision,
    long RestaurantId,
    DeduplicationResult Deduplication,
    string Reason);

/// <summary>Resultado da classificação por IA, já validada contra o schema esperado.</summary>
public sealed record AiClassification(
    string? Category,
    string? Neighborhood,
    string? City,
    double Confidence,
    AiProvider Provider,
    bool UsedFallback)
{
    /// <summary>
    /// Abaixo deste valor a sugestão vai para revisão humana em vez de ser aplicada.
    /// A Cuia IA nunca publica sozinha.
    /// </summary>
    public const double AutoApplyThreshold = 0.85;

    public bool CanAutoApply => Confidence >= AutoApplyThreshold;
}

/// <summary>Divergência detectada entre fontes.</summary>
public sealed record ConsistencyFinding(
    long RestaurantId,
    string Field,
    string? ValueFromSourceA,
    string? ValueFromSourceB,
    string Severity)
{
    public static ConsistencyFinding Warning(long id, string field, string? a, string? b) =>
        new(id, field, a, b, "warning");

    public static ConsistencyFinding Critical(long id, string field, string? a, string? b) =>
        new(id, field, a, b, "critical");
}

/// <summary>Estado de um conector, mostrado no painel de fontes.</summary>
public sealed record ConnectorHealth(
    string Name,
    string State,
    bool Configured,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    decimal? MonthlyCostUsd);

public static class ConnectorStateNames
{
    public const string NotConfigured = "nao_configurado";
    public const string Active = "ativo";
    public const string Degraded = "degradado";
    public const string Paused = "pausado";
}
