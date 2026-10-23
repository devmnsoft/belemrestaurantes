namespace BelemRestaurantes.Application.Abstractions;

public sealed record PlaceReference(long Id,string PlaceName,string City,string SourceUrl,string SourceKind,
    string Publisher,DateTimeOffset ObservedAt,DateTimeOffset ExpiresAt,IReadOnlyList<string> MenuItems,
    string? Address=null,IReadOnlyList<string>? VideoLinks=null,string Status="pending",IReadOnlyList<string>? OpeningHours=null);

public interface IPlaceReferences
{
    Task<IReadOnlyList<PlaceReference>> FindAsync(string question,string? city,CancellationToken ct);
    Task<long> ObserveAsync(PlaceReference reference,CancellationToken ct);
    Task<IReadOnlyList<PlaceReference>> ListPendingAsync(CancellationToken ct);
    Task<bool> ReviewAsync(long id,string decision,string actor,CancellationToken ct);
}
