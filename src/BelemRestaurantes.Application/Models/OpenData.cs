namespace BelemRestaurantes.Application.Models;

/// <summary>Números honestos do catálogo publicado, para a página de dados abertos.</summary>
public sealed record OpenDataSummary(
    int PublishedTotal,
    int WithCoordinates,
    int GuideTier,
    IReadOnlyList<OpenDataCityCount> Cities,
    DateTimeOffset GeneratedAtUtc);

public sealed record OpenDataCityCount(string City, int Total);

/// <summary>Linha do catálogo publicada para exportação aberta (GeoJSON/CSV). Só campos já públicos na ficha.</summary>
/// <remarks>Propriedades <c>init</c> (não construtor primário) porque o mapeamento do Dapper por colunas usa o type map de propriedades.</remarks>
public sealed record OpenDataCatalogRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public string? Neighborhood { get; init; }
    public string? Category { get; init; }
    public string? Address { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string CatalogTier { get; init; } = "";
    public string BusinessStatus { get; init; } = "";
    public string? OpeningHours { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
}
