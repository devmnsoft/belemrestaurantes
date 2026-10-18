namespace BelemRestaurantes.Application.Abstractions;

public sealed record OpenDataSnapshot(string City, string Payload, DateTimeOffset FetchedAt);

public interface IOpenDataCache
{
    Task<OpenDataSnapshot?> GetAsync(string city, CancellationToken ct);
    Task SaveAsync(OpenDataSnapshot snapshot, CancellationToken ct);
    Task<IReadOnlyList<OpenDataSnapshot>> ListAsync(CancellationToken ct);
}
