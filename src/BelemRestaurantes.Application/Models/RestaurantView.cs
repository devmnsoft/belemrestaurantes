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
    DateTimeOffset UpdatedAt);

/// <summary>Filtros da busca pública. Todos os campos são validados e parametrizados.</summary>
public sealed record RestaurantSearchQuery(
    string? Term = null,
    string? City = null,
    string? Neighborhood = null,
    string? Category = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int Page = 1,
    int PageSize = 24)
{
    /// <summary>Limites aplicados pela regra, não pelo controller.</summary>
    public int SafePage => Math.Clamp(Page, 1, 10_000);

    public int SafePageSize => Math.Clamp(PageSize, 1, 60);

    public string? SafeTerm => string.IsNullOrWhiteSpace(Term) ? null : Term.Trim();

    public string? SafeCity => string.IsNullOrWhiteSpace(City) ? null : City.Trim();
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
    string? AiSuggestion);