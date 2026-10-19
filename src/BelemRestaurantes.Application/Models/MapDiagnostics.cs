using System.Diagnostics.Metrics;
namespace BelemRestaurantes.Application.Models;

public static class MapDiagnostics
{
    private static readonly Meter Meter = new("BelemRestaurantes.Maps", "1.0");
    public static readonly Histogram<double> QueryMilliseconds = Meter.CreateHistogram<double>("map.sql.duration", "ms");
    public static readonly Histogram<double> RoutesMilliseconds = Meter.CreateHistogram<double>("map.routes.duration", "ms");
}
