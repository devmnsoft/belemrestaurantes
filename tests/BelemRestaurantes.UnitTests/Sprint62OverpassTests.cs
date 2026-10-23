using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Crawler;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint62OverpassTests
{
    private const string FichaComNumero = "Travessa dos Tamoios 357";
    private const string FichaSemNumero = "Travessa dos Tamoios";

    private static NominatimCandidate Road(string road, long id, double latitude = -1.47, double longitude = -48.5, string? state = "Pará") =>
        new(latitude, longitude, "residential", id, road, null, "Belém", state);

    private static OsmBuildingElement Building(long id, string? street, string? house, double latitude = -1.471, double longitude = -48.501, string kind = "way") =>
        new(id, kind, latitude, longitude, street, house);

    [Fact]
    public void ParseElements_ReadsNodesWaysRelationsAndAddrTags()
    {
        var json = """
            {"elements":[
              {"type":"node","id":101,"lat":-1.4710,"lon":-48.5010,"tags":{"addr:street":"Travessa dos Tamoios","addr:housenumber":"357"}},
              {"type":"way","id":102,"center":{"lat":-1.4712,"lon":-48.5012},"tags":{"building":"yes"}},
              {"type":"relation","id":103,"center":{"lat":-1.4715,"lon":-48.5015},"tags":{"amenity":"restaurant"}},
              {"type":"node","id":104}
            ]}
            """;
        var elements = OverpassBuildingSource.ParseElements(json);
        Assert.Equal(3, elements.Count);
        var first = elements[0];
        Assert.Equal((101L, "node"), (first.OsmId, first.Kind));
        Assert.Equal("Travessa dos Tamoios", first.StreetTag);
        Assert.Equal("357", first.HouseNumberTag);
        Assert.Equal((null, null), (elements[1].StreetTag, elements[1].HouseNumberTag)); // apenas building:"yes"
        Assert.Equal("relation", elements[2].Kind);
    }

    [Fact]
    public void ParseElements_InvalidOrUnexpectedJsonIsEmpty()
    {
        Assert.Empty(OverpassBuildingSource.ParseElements(""));
        Assert.Empty(OverpassBuildingSource.ParseElements("{isso nao é json"));
        Assert.Empty(OverpassBuildingSource.ParseElements("{\"elements\":[]}"));
        Assert.Empty(OverpassBuildingSource.ParseElements("{\"nenhum\":\"elements\"}"));
    }

    [Fact]
    public void BuildQuery_UsesMultilineUnion_PublicInstancesRejectSingleLine()
    {
        var query = OverpassBuildingSource.BuildQuery(-1.458131, -48.494855, 80);
        // as instâncias públicas (OSM3S) rejeitam a união em linha única com parse error — trava o formato
        Assert.Contains("\n(", query, StringComparison.Ordinal);
        Assert.DoesNotContain("(\n", query.Replace("[out:json][timeout:20];\n(\n", ""), StringComparison.Ordinal);
        Assert.Contains("nwr[\"building\"](around:80,-1.458131,-48.494855);", query, StringComparison.Ordinal);
        Assert.Contains("nwr[\"addr:housenumber\"](around:80,-1.458131,-48.494855);", query, StringComparison.Ordinal);
        Assert.EndsWith("out center tags;", query, StringComparison.Ordinal);
    }

    [Fact]
    public void FindStreetAnchor_SingleMatchingRoadInPará_ReturnsIt()
    {
        var candidates = new[]
        {
            Road("TRAVESSA DOS Tamoios", 900),
            Road("Avenida Brasil", 777) // outro logradouro não ancora
        };
        var anchor = GeocodingRules.FindStreetAnchor(FichaComNumero, candidates);
        Assert.NotNull(anchor);
        Assert.Equal(900, anchor!.PlaceId);
    }

    [Fact]
    public void FindStreetAnchor_MultipleMatchesOnSameStreet_UsesFirstInRelevanceOrder()
    {
        // vários objetos no mesmo logradouro não impedem a âncora: o primeiro (relevância do Nominatim) ancora
        var candidates = new[] { Road("Travessa dos Tamoios", 900, -1.470), Road("Travessa dos Tamoios", 901, -1.475) };
        var anchor = GeocodingRules.FindStreetAnchor(FichaComNumero, candidates);
        Assert.NotNull(anchor);
        Assert.Equal(900, anchor!.PlaceId);
    }

    [Fact]
    public void FindStreetAnchor_FirstMatchSkippedWhenAnotherValid_FollowsOrder()
    {
        // primeiro candidato fora da caixa é pulado; o próximo dentro ancora
        var candidates = new[]
        {
            Road("Travessa dos Tamoios", 888, -11.0, -48.5),
            Road("Travessa dos Tamoios", 902, -1.471)
        };
        var anchor = GeocodingRules.FindStreetAnchor(FichaComNumero, candidates);
        Assert.NotNull(anchor);
        Assert.Equal(902, anchor!.PlaceId);
    }

    [Fact]
    public void FindStreetAnchor_RoadNameMismatch_ReturnsNull()
    {
        var candidates = new[] { Road("Rua do Comércio", 900) };
        Assert.Null(GeocodingRules.FindStreetAnchor(FichaComNumero, candidates));
    }

    [Theory]
    [InlineData(-11.0, -48.5)]  // fora da caixa (sul demais)
    [InlineData(-1.47, -45.0)]  // fora da caixa (leste demais)
    public void FindStreetAnchor_CoordinatesOutOfRange_ReturnsNull(double latitude, double longitude)
    {
        var candidates = new[] { Road("Travessa dos Tamoios", 900, latitude, longitude) };
        Assert.Null(GeocodingRules.FindStreetAnchor(FichaComNumero, candidates));
    }

    [Fact]
    public void FindStreetAnchor_EmptyList_ReturnsNull()
    {
        Assert.Null(GeocodingRules.FindStreetAnchor(FichaComNumero, []));
    }

    [Fact]
    public void EvaluateBuildings_WithHouseNumber_SingleConfirmedObject_AcceptsRooftop()
    {
        var elements = new[]
        {
            Building(101, "Travessa dos Tamoios", "357"),
            Building(102, "Travessa dos Tamoios", "355")
        };
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", elements);
        Assert.True(decision.Accepted);
        Assert.Equal(("rooftop", 101L), (decision.Precision, decision.PlaceId)); // objeto confirmado é o 357
    }

    [Fact]
    public void EvaluateBuildings_WithHouseNumber_WrongStreetTag_RejectsNoMatch()
    {
        var elements = new[] { Building(101, "Rua Nova", "357") };
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", elements);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_no_match", decision.Reason);
    }

    [Fact]
    public void EvaluateBuildings_WithHouseNumber_TwoConfirmedObjects_AcceptsNothing()
    {
        var elements = new[]
        {
            Building(101, "Travessa dos Tamoios", "357", -1.470, -48.500),
            Building(102, "Travessa dos Tamoios", "357", -1.472, -48.504)
        };
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", elements);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_ambiguous", decision.Reason);
    }

    [Fact]
    public void EvaluateBuildings_WithHouseNumber_DuplicatedGeometryCountsOnce()
    {
        // mesma geometria reportada como way e relation: um único físico
        var elements = new[]
        {
            Building(101, "Travessa dos Tamoios", "357", -1.471, -48.501, "way"),
            Building(202, "Travessa dos Tamoios", "357", -1.47100004, -48.50100003, "relation")
        };
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", elements);
        Assert.True(decision.Accepted);
        Assert.Equal("rooftop", decision.Precision);
    }

    [Fact]
    public void EvaluateBuildings_WithoutHouseNumber_SingleObjectOnStreet_AcceptsApproximate()
    {
        var elements = new[] { Building(101, "Travessa dos Tamoios", null) };
        var decision = GeocodingRules.EvaluateBuildings(FichaSemNumero, "Belém", elements);
        Assert.True(decision.Accepted);
        Assert.Equal("approximate", decision.Precision);
    }

    [Fact]
    public void EvaluateBuildings_WithoutHouseNumber_TwoObjectsOnStreet_AcceptsNothing()
    {
        var elements = new[]
        {
            Building(101, "Travessa dos Tamoios", null, -1.470, -48.500),
            Building(102, "Travessa dos Tamoios", null, -1.473, -48.505)
        };
        var decision = GeocodingRules.EvaluateBuildings(FichaSemNumero, "Belém", elements);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_ambiguous", decision.Reason);
    }

    [Fact]
    public void EvaluateBuildings_WithoutHouseNumber_ConflictingSingleStreetTag_Rejects()
    {
        var elements = new[] { Building(101, "Avenida Brasil", null) };
        var decision = GeocodingRules.EvaluateBuildings(FichaSemNumero, "Belém", elements);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_no_match", decision.Reason);
    }

    [Fact]
    public void EvaluateBuildings_AllOutsideParáBox_Rejects()
    {
        var elements = new[] { Building(101, "Travessa dos Tamoios", "357", -11.0, -48.5) };
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", elements);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_no_match", decision.Reason);
    }

    [Fact]
    public void EvaluateBuildings_EmptyList_RejectsNoMatch()
    {
        var decision = GeocodingRules.EvaluateBuildings(FichaComNumero, "Belém", []);
        Assert.False(decision.Accepted);
        Assert.Equal("overpass_no_match", decision.Reason);
    }

    [Fact]
    public async Task BuildingsNear_FirstEndpointFails_FallsBackToNext()
    {
        var options = new OverpassOptions
        {
            Endpoints = new[] { "https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter" },
            MinimumIntervalMilliseconds = 0
        };
        var freshStamp = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var payload = "{\"osm3s\":{\"timestamp_osm_base\":\"" + freshStamp + "\"},\"elements\":[{\"type\":\"way\",\"id\":55,\"center\":{\"lat\":-1.4700,\"lon\":-48.5000},\"tags\":{\"addr:street\":\"Travessa dos Tamoios\",\"addr:housenumber\":\"357\"}}]}";
        var http = new StubHttp(url => url.Contains("overpass-api.de")
            ? Fail(406)
            : Json(payload));
        var result = await new OverpassBuildingSource(http, options).BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(result.SourceAvailable);
        Assert.False(result.DataStale);
        Assert.Single(result.Elements);
        Assert.Equal(55, result.Elements[0].OsmId);
    }

    [Fact]
    public async Task BuildingsNear_AllEndpointsFail_ReturnsUnavailableWithReasons()
    {
        var options = new OverpassOptions
        {
            Endpoints = new[] { "https://instancia-a.de/api/interpreter", "https://instancia-b.org/api/interpreter" },
            MinimumIntervalMilliseconds = 0
        };
        var http = new StubHttp(url => url.Contains("instancia-a.de") ? Fail(406) : Fail(500));
        var result = await new OverpassBuildingSource(http, options).BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.False(result.SourceAvailable);
        Assert.StartsWith("unavailable(", result.FailureReason, StringComparison.Ordinal);
        Assert.Empty(result.Elements);
    }

    [Theory]
    [InlineData("2026-10-20T02:16:18Z", false)] // 2 dias -> fresco
    [InlineData("2026-07-28T02:16:18Z", true)]  // ~86 dias (snapshot observado na kumi.systems) -> envelhecido
    [InlineData("117921", true)]                // formato ilegível (observado na overpass.osm.ch) -> conservador
    public void IsDataStale_ParsesBaseTimestamp(string stamp, bool expectedStale)
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expectedStale, OverpassBuildingSource.IsDataStale(Osm3sJson(stamp), now));
    }

    [Fact]
    public void IsDataStale_FifteenDaysBoundary_IsStale()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.False(OverpassBuildingSource.IsDataStale(Osm3sJson("2026-10-08T12:00:00Z"), now)); // exatamente 14 dias -> ainda vale
        Assert.True(OverpassBuildingSource.IsDataStale(Osm3sJson("2026-10-07T11:59:59Z"), now));  // um segundo além -> envelhecido
    }

    [Fact]
    public void IsDataStale_EpochSeconds_IsParsed()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.False(OverpassBuildingSource.IsDataStale(Osm3sJson(now.AddDays(-2).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)), now));
        Assert.True(OverpassBuildingSource.IsDataStale(Osm3sJson(now.AddDays(-30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)), now));
    }

    [Fact]
    public void IsDataStale_MissingOrBrokenPayload_IsConservativelyStale()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.True(OverpassBuildingSource.IsDataStale("{\"elements\":[]}", now));   // sem osm3s
        Assert.True(OverpassBuildingSource.IsDataStale("{\"osm3s\":{}}", now));      // sem o campo
        Assert.True(OverpassBuildingSource.IsDataStale("{isso nao e json", now));    // json inválido
    }

    private static string Osm3sJson(string stamp) => $"{{\"osm3s\":{{\"timestamp_osm_base\":\"{stamp}\"}},\"elements\":[]}}";

    private sealed class StubHttp(Func<string, SafeDownloadResult> handler) : ISafeHttpClient
    {
        public Task<SafeDownloadResult> DownloadAsync(string url, string? etag = null, string? lastModified = null, int timeoutSeconds = 15, CancellationToken ct = default)
            => Task.FromResult(handler(url));
    }

    private static SafeDownloadResult Json(string text) => new(true, 200, text, "application/json", null, null, null);
    private static SafeDownloadResult Fail(int code) => new(false, code, null, "text/html", null, null, $"http {code}");
}
