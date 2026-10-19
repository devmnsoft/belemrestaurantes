using System.Globalization;

namespace BelemRestaurantes.Application.Models;

public sealed record MapBounds(double West, double South, double East, double North)
{
    public static bool TryParse(string? value, out MapBounds? bounds)
    {
        bounds = null;
        var parts = value?.Split(',');
        if (parts?.Length != 4) return false;
        var numbers = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i])) return false;
        if (numbers[0] < -180 || numbers[2] > 180 || numbers[1] < -90 || numbers[3] > 90 || numbers[0] >= numbers[2] || numbers[1] >= numbers[3]) return false;
        bounds = new(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}

public sealed record MapQuery(string? City = null, string? Term = null, string? Category = null,
    string? Neighborhood = null, string? Tier = null, MapBounds? Bounds = null, long After = 0, int Limit = 100)
{
    public int SafeLimit => Math.Clamp(Limit, 1, 200);
    public long SafeAfter => Math.Max(0, After);
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 80)];
}
public sealed record MapPointPage(IReadOnlyList<CatalogMapPoint> Points, long? Next);
public sealed record MapSummary(int Total, int Guides, IReadOnlyList<string> Cities);
