namespace BelemRestaurantes.Application.Models;

public sealed record LicensedPhotoCandidate(
    string MediaUrl,
    string? ThumbnailUrl,
    string SourcePage,
    string Attribution,
    string LicenseType,
    bool StrongMatch);

public sealed record LicensedPhotoLookup(bool Available, string? Error, IReadOnlyList<LicensedPhotoCandidate> Photos)
{
    public static LicensedPhotoLookup Failed(string error) => new(false, error, []);
    public static LicensedPhotoLookup Empty { get; } = new(true, null, []);
}
