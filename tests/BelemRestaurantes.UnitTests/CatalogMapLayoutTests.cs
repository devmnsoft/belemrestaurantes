using BelemRestaurantes.Application.Models;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public sealed class CatalogMapLayoutTests
{
    [Fact]
    public void Plot_PlacesNorthAboveSouth_AndSkipsNullIsland()
    {
        var points = new CatalogMapPoint[]
        {
            new() { Id = 1, Name = "Norte", City = "Belém", Neighborhood = "Guamá", Category = "Açaí", Latitude = -1.40, Longitude = -48.45, CatalogTier = "ficha" },
            new() { Id = 2, Name = "Sul", City = "Belém", Neighborhood = "Cidade Velha", Category = "Tacacá", Latitude = -1.46, Longitude = -48.50, CatalogTier = "guia" },
            new() { Id = 3, Name = "Inválido", City = "Belém", Latitude = 0, Longitude = 0, CatalogTier = "ficha" }
        };

        var plotted = CatalogMapLayout.Plot(points);

        Assert.Equal(2, plotted.Count);
        var north = Assert.Single(plotted, point => point.Place.Id == 1);
        var south = Assert.Single(plotted, point => point.Place.Id == 2);
        Assert.True(north.Y < south.Y);
        Assert.True(north.X > south.X);
        Assert.InRange(north.X, 0, CatalogMapLayout.Width);
        Assert.InRange(south.Y, 0, CatalogMapLayout.Height);
        Assert.False(CatalogMapLayout.IsPlottable(0, 0));
    }
}
