using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Abstractions;

public interface ILicensedPhotoFinder
{
    Task<LicensedPhotoLookup> FindAsync(string restaurantName, string city, CancellationToken ct);
}

public interface ISearchGapWriter
{
    Task RecordAsync(string? term, string? city, int resultCount, CancellationToken ct);
}

public interface ITasteGuide
{
    Task<TasteGuidePage?> GetAsync(string slug, CancellationToken ct);
}
