using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.UnitTests;

public sealed class MapQueryTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("NaN,-2,-48,0")]
    [InlineData("-48,-2,-49,0")] [InlineData("-181,-2,-48,0")]
    [InlineData("-49,-91,-48,0")] [InlineData("-49,-2,181,0")]
    [InlineData("-49,-2,-48,91")] [InlineData("-49,2,-48,-2")]
    [InlineData("1,2,3")] [InlineData("Infinity,0,1,2")]
    public void RejectsInvalidBounds(string? value) => Assert.False(MapBounds.TryParse(value, out _));

    [Fact]
    public void ParsesInvariantCoordinatesAndBoundsLimits()
    {
        Assert.True(MapBounds.TryParse("-49,-2,-48,0", out var bounds));
        Assert.Equal(new MapBounds(-49, -2, -48, 0), bounds);
        Assert.Equal(200, new MapQuery(Limit: 50000).SafeLimit);
        Assert.Equal(1, new MapQuery(Limit: -1).SafeLimit);
        Assert.Equal(0, new MapQuery(After: -1).SafeAfter);
    }
}
