namespace BelemRestaurantes.Web.Models;

public sealed record CuiaBudgetRow(string Provider, decimal MonthUsd, bool BudgetRemaining);

public sealed record CuiaDiagnosticsView(
    IReadOnlyList<CuiaAiProviderStatus> Providers,
    IReadOnlyList<ProviderHealthReport> Health,
    IReadOnlyList<CuiaBudgetRow> Budgets,
    IReadOnlyList<AiCostActivity> Recent,
    CuiaAskSnapshot Metrics,
    string? LedgerNote,
    string RateLimitPolicy);
