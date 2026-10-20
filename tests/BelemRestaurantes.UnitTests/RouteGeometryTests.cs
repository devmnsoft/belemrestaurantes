using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.Mobility;

namespace BelemRestaurantes.UnitTests;

public sealed class RouteGeometryTests
{
    [Fact]
    public void DecodesOfficialPrecisionFiveExample()
    {
        var points=RouteGeometry.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        Assert.Equal(3,points.Count);
        Assert.Equal(new RouteCoordinate(38.5,-120.2),points[0]);
        Assert.Equal(new RouteCoordinate(40.7,-120.95),points[1]);
        Assert.Equal(new RouteCoordinate(43.252,-126.453),points[2]);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("_")] [InlineData("~~~~~~~")] [InlineData("??")]
    [InlineData("<script>")]
    public void RejectsTruncatedMalformedAndSinglePointGeometry(string? encoded)=>Assert.Empty(RouteGeometry.Decode(encoded));

    [Fact]
    public void ProviderPreservesRouteStepsDirectionStopsAndGeometryWithoutInventingFare()
    {
        using var json=JsonDocument.Parse("""
        {"routes":[{"duration":"1200s","distanceMeters":4200,"polyline":{"encodedPolyline":"_p~iF~ps|U_ulLnnqC_mqNvxq`@"},"legs":[{"steps":[
          {"travelMode":"WALK","distanceMeters":200,"staticDuration":"180s","polyline":{"encodedPolyline":"_p~iF~ps|U_ulLnnqC_mqNvxq`@"}},
          {"travelMode":"TRANSIT","distanceMeters":4000,"transitDetails":{"headsign":"Sentido fixture","transitLine":{"nameShort":"TESTE"},"stopDetails":{"departureStop":{"name":"Embarque fixture","location":{"latLng":{"latitude":-1.45,"longitude":-48.49}}},"arrivalStop":{"name":"Desembarque fixture"}}}}
        ]}]}]}
        """);
        var route=Assert.Single(GoogleRoutesProvider.Parse(json.RootElement,1000));
        Assert.Equal(3,route.Path.Count);Assert.NotNull(route.EncodedPolyline);Assert.Null(route.Fare);
        Assert.Equal("Sentido fixture",route.Steps[1].Headsign);
        Assert.Equal(new RouteCoordinate(-1.45,-48.49),route.Steps[1].BoardLocation);
        Assert.Equal(180,route.Steps[0].DurationSeconds);Assert.Equal(0,route.Transfers);
        Assert.Empty(GoogleRoutesProvider.Parse(json.RootElement,100));
    }
    [Theory] [InlineData("null")] [InlineData("[]")] [InlineData("{\"routes\":[{\"duration\":\"NaNs\"}]}")]
    public void InvalidProviderRootOrDurationDoesNotBecomeARoute(string payload)
    {using var json=JsonDocument.Parse(payload);Assert.Empty(GoogleRoutesProvider.Parse(json.RootElement,1000));}
}
