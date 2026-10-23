namespace BelemRestaurantes.Application.Abstractions;

public sealed record CulinaryReference(long Id, string Kind, string Title, string Url, string Author,
    string Summary, string[] Ingredients, string? VideoUrl, string? PlaceName, string? City,
    string? Address, string[] SocialLinks, DateTime ObservedAt, DateTime? PublishedAt,
    string Evidence, string? Yield, decimal? Price = null, string? Currency = null);

public sealed record CulinaryCollection(int Discovered, int Published, int Deferred);

public interface ICulinaryResearch
{
    Task<IReadOnlyList<CulinaryReference>> FindAsync(string query, CancellationToken ct);
    Task<CulinaryReference?> GetAsync(long id, CancellationToken ct);
    Task EnqueueAsync(IEnumerable<string> urls, string topic, CancellationToken ct);
    Task<CulinaryCollection> CollectAsync(CancellationToken ct);
}
