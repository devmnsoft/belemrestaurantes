namespace BelemRestaurantes.Application.Abstractions;

public sealed record FieldProvenance(string Field, string? Value, string State, string? SourceName, string? SourceUrl, DateTime CollectedAt, string? Evidence);

public sealed record PlaceDish(string Name, string Attribution, string? Note, string? SourceUrl, string? Availability);

public sealed record CulinaryVideo(long Id, string Title, string Author, string Platform, string Url, DateOnly? PublishedOn, string Summary, string Attribution, string? Topic);

public sealed record PlaceDossier(IReadOnlyList<FieldProvenance> Fields, IReadOnlyList<PlaceDish> Dishes, IReadOnlyList<CulinaryVideo> Videos);

public interface IPlaceDossier
{
    Task<PlaceDossier> ForRestaurantAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<CulinaryVideo>> ListVideosAsync(string? topic, CancellationToken ct);
}
