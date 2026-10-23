namespace BelemRestaurantes.Application.Abstractions;

public sealed record SearchTopic(string Label, long Count);
public interface ISearchArchive
{
    Task<WebSearchResult?> GetAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, WebSearchResult result, CancellationToken ct);
    Task RecordTopicsAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<SearchTopic>> TopicsAsync(CancellationToken ct);
}
