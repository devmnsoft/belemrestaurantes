namespace BelemRestaurantes.Application.Abstractions;

public sealed record WebSearchSource(string Title, string Url, string Snippet, DateTimeOffset RetrievedAt)
{
    public string? Publisher { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string SourceKind { get; init; } = "search_snippet";
    public string RetrievalStatus { get; init; } = "snippet_only";
    public string? PlaceName { get; init; }
    public string? City { get; init; }
    public string? Address { get; init; }
    public IReadOnlyList<string> MenuItems { get; init; } = [];
    public IReadOnlyList<string> OpeningHours { get; init; } = [];
}

public interface IResearchEvidence
{
    Task<WebSearchResult> EnrichAsync(WebSearchResult search, string city, CancellationToken ct);
}
public sealed record WebSearchResult(string Status, IReadOnlyList<WebSearchSource> Sources)
{
    public string? Provider {get;init;}
    public string? FailureCode {get;init;}
}

public interface IWebSearch
{
    Task<WebSearchResult> SearchAsync(string query, CancellationToken ct);
}

/// <summary>Reserves attempts before external calls. Failed calls still consume a reservation.</summary>
public interface IFreeTierQuota
{
    Task<bool> TryReserveAsync(string service, int dailyLimit, int monthlyLimit, CancellationToken ct);
    Task<bool> TryReserveUnitsAsync(string service, int dailyLimit, int monthlyLimit, int units, CancellationToken ct) => Task.FromResult(false);
}

public sealed class CuiaFreeTierOptions
{
    // A key alone does not identify a free account. Configure only verified free accounts.
    public string[] ConfirmedFreeProviders { get; set; } = [];
    public string[] ConfirmedPlanProviders {get;set;} = [];
    public int DailyRequestLimit { get; set; } = 100;
    public int MonthlyRequestLimit { get; set; } = 1000;
    public int WorkerDailyRequestLimit {get;set;} = 2;
    public int WorkerMonthlyRequestLimit {get;set;} = 20;
    public int SessionTokenLimit { get; set; } = 24000;
    public int MaxOutputTokens { get; set; } = 800;
}
