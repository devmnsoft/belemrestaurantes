namespace BelemRestaurantes.Application.Models;

/// <summary>Ponto publicado com coordenada. Não inclui cadastro pendente.</summary>
public sealed class CatalogMapPoint
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string? Neighborhood { get; set; }
    public string? Category { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string CatalogTier { get; set; } = "ficha";
}

/// <summary>O que a ficha pública realmente tem. Ausência fica explícita.</summary>
public sealed record CatalogFactSheet(
    bool HasAddress,
    bool HasCoordinates,
    bool HasPhone,
    bool HasPhoto,
    bool HasHours,
    bool HasMenu,
    bool HasOwner);

public sealed record PlottedCatalogPoint(CatalogMapPoint Place, double X, double Y);

/// <summary>Projeção equiretangular das coordenadas publicadas. Não usa tiles de terceiros.</summary>
public static class CatalogMapLayout
{
    public const double Width = 800;
    public const double Height = 640;

    public static bool IsPlottable(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
        && !(Math.Abs(latitude) < 0.000001 && Math.Abs(longitude) < 0.000001);

    public static IReadOnlyList<PlottedCatalogPoint> Plot(IReadOnlyList<CatalogMapPoint> points)
    {
        var valid = points.Where(point => IsPlottable(point.Latitude, point.Longitude)).ToList();
        if (valid.Count == 0) return [];

        var minLat = valid.Min(point => point.Latitude);
        var maxLat = valid.Max(point => point.Latitude);
        var minLng = valid.Min(point => point.Longitude);
        var maxLng = valid.Max(point => point.Longitude);
        var latPad = Math.Max((maxLat - minLat) * 0.12, 0.01);
        var lngPad = Math.Max((maxLng - minLng) * 0.12, 0.01);
        minLat -= latPad;
        maxLat += latPad;
        minLng -= lngPad;
        maxLng += lngPad;
        var latSpan = maxLat - minLat;
        var lngSpan = maxLng - minLng;
        if (latSpan <= 0 || lngSpan <= 0) return [];

        return valid.Select(point => new PlottedCatalogPoint(
            point,
            (point.Longitude - minLng) / lngSpan * Width,
            (maxLat - point.Latitude) / latSpan * Height)).ToList();
    }
}
