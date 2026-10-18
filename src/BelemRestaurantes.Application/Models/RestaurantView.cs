namespace BelemRestaurantes.Application.Models;

/// <summary>Estabelecimento como o site o enxerga. Somente itens publicados chegam aqui.</summary>
public sealed record RestaurantView(
    long Id,
    string Name,
    string City,
    string? Neighborhood,
    string? Category,
    string? Address,
    string? Website,
    string? Instagram,
    string? Facebook,
    string? TikTok,
    string? YouTube,
    string? PhotoUrl,
    DateTimeOffset UpdatedAt)
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceKm { get; init; }
    public string? Phone { get; init; }
    public string? BusinessStatus { get; init; }
    public string? SourceUrl { get; init; }
    public RestaurantView() : this(0, "", "", null, null, null, null, null, null, null, null, null, default) { }
}

/// <summary>Filtros da busca pública. Todos os campos são validados e parametrizados.</summary>
public sealed record RestaurantSearchQuery(
    string? Term = null,
    string? City = null,
    string? Neighborhood = null,
    string? Category = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int Page = 1,
    int PageSize = 24,
    string? Sort = null,
    bool PhotosOnly = false,
    double? Latitude = null,
    double? Longitude = null,
    double RadiusKm = 10)
{
    /// <summary>Limites aplicados pela regra, não pelo controller.</summary>
    public int SafePage => Math.Clamp(Page, 1, 10_000);

    public int SafePageSize => Math.Clamp(PageSize, 1, 60);

    public string? SafeTerm => string.IsNullOrWhiteSpace(Term) ? null : Term.Trim();

    public string? SafeCity => string.IsNullOrWhiteSpace(City) ? null : City.Trim();
    public bool HasLocation => Latitude is { } lat && Longitude is { } lng && double.IsFinite(lat) && double.IsFinite(lng)
        && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
    public double SafeRadiusKm => double.IsFinite(RadiusKm) ? Math.Clamp(RadiusKm, 1, 100) : 10;
    public string SafeSort => Sort is "recent" or "city" ? Sort : Sort == "nearby" && HasLocation ? "nearby" : "name";
}

/// <summary>Resultado paginado.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Estabelecimento devolvido por uma fonte de descoberta.</summary>
public sealed record DiscoveredPlace(
    string ExternalId,
    string Name,
    string? Neighborhood,
    string? City,
    string? Category,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? GoogleMapsUri,
    string? Website,
    int? GoogleRating,
    int? GoogleRatingCount,
    IReadOnlyList<string> Types)
{
    public string? Phone { get; init; }
    public string? State { get; init; }
    public string? BusinessStatus { get; init; }
    public string? Instagram { get; init; }
    public string? Facebook { get; init; }
    public string? TikTok { get; init; }
    public string? YouTube { get; init; }

    /// <summary>Nome normalizado, usado como chave de deduplicação.</summary>
    public string NormalizedName => NameNormalizer.Normalize(Name);

    public string ResolvedCity => string.IsNullOrWhiteSpace(City) ? string.Empty : City.Trim();
}

/// <summary>Resultado de uma pesquisa textual numa fonte.</summary>
public sealed record PlaceSearchResult(
    IReadOnlyList<DiscoveredPlace> Places,
    string? NextPageToken,
    DateTimeOffset QueriedAt,
    int ProviderChargedUnits = 0);

/// <summary>Estabelecimento recém-descoberto aguardando decisão humana.</summary>
public sealed record PendingApproval(
    long Id,
    string Name,
    string City,
    string? Neighborhood,
    string? Category,
    string? GooglePlaceId,
    string? SourceUrl,
    double DedupConfidence,
    string? ConflictReason,
    DateTimeOffset DiscoveredAt,
    string? AiSuggestion)
{
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public PendingApproval() : this(0, "", "", null, null, null, null, 0, null, default, null) { }
}
