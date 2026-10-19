using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Web.Models;

public sealed record CatalogMapPage(
    IReadOnlyList<CatalogMapPoint> Places,
    IReadOnlyList<PlottedCatalogPoint> Plotted,
    IReadOnlyList<string> Cities,
    int PublishedWithCoordinates,
    int GuideCount,
    string? City,
    string? Term);
