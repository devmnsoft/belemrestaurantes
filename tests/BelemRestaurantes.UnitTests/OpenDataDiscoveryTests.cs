using System.Net;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.OpenData;
using Microsoft.Extensions.Logging.Abstractions;

namespace BelemRestaurantes.UnitTests;

public sealed class OpenDataDiscoveryTests
{
    [Theory]
    [InlineData("Feira do Bairro", "marketplace", "", "", "Feira e mercado")]
    [InlineData("Açaí do Rio", "fast_food", "", "brazilian", "Açaí")]
    [InlineData("Casa do Norte", "restaurant", "", "acai;fish", "Açaí")]
    [InlineData("Pizza da Ilha", "fast_food", "", "italian;pizza", "Pizzaria")]
    [InlineData("Padaria do Bairro", "", "bakery", "", "Padaria")]
    [InlineData("Doces da Vila", "", "confectionery", "", "Confeitaria")]
    [InlineData("Horta da Vila", "", "greengrocer", "", "Hortifruti")]
    [InlineData("Lanche da Praça", "fast_food", "", "", "Lanchonete")]
    [InlineData("Bolos da Vila", "", "pastry", "", "Confeitaria")]
    public void ExtendedFoodCategoriesPreserveSourceMeaning(string name, string amenity, string shop, string cuisine, string expected)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { elements = new[] {
            new { type="node", id=101, lat=-1.45, lon=-48.49, tags = new Dictionary<string,string> {
                ["name"]=name,["amenity"]=amenity,["shop"]=shop,["cuisine"]=cuisine } } } });
        Assert.Equal(expected, Assert.Single(OpenStreetMapDiscoveryClient.Parse(payload,"Belém")).Category);
    }

    [Fact]
    public void LargerSnapshotsAreNotSilentlyTruncatedAt120_AndDuplicateIdsAreRemoved()
    {
        var elements = Enumerable.Range(1, 500).Select(id => new { type="node", id, lat=-1.45, lon=-48.49,
            tags=new { name=$"Lanche de teste {id}", amenity="fast_food" } }).ToArray();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { elements=elements.Concat(elements.Take(1)) });
        Assert.Equal(500,OpenStreetMapDiscoveryClient.Parse(payload,"Belém").Count);
    }

    [Fact]
    public void ParserRejectsUnrelatedShopsAndClosedPlaces()
    {
        const string payload = """
            {"elements":[
            {"type":"node","id":1,"lat":-1.45,"lon":-48.49,"tags":{"name":"Farmácia","shop":"chemist"}},
            {"type":"node","id":2,"lat":-1.45,"lon":-48.49,"tags":{"name":"Antigo Café","amenity":"cafe","disused":"yes"}},
            {"type":"node","id":3,"lat":-25,"lon":-48.49,"tags":{"name":"Fora do Pará","amenity":"restaurant"}}
            ]}
            """;
        Assert.Empty(OpenStreetMapDiscoveryClient.Parse(payload,"Belém"));
        Assert.Throws<ArgumentException>(()=>OpenStreetMapDiscoveryClient.BuildQuery("São Paulo"));
        Assert.Contains("marketplace",OpenStreetMapDiscoveryClient.BuildQuery("Belém"));
    }

    private const string Payload = """
        {"elements":[
          {"type":"node","id":1,"lat":-1.45,"lon":-48.49,"tags":{"name":"Pizza da Feira","amenity":"restaurant","cuisine":"pizza","addr:street":"Rua da Feira","addr:housenumber":"10","phone":"(91) 3333-4444","website":"javascript:alert(1)"}},
          {"type":"way","id":2,"center":{"lat":-1.44,"lon":-48.48},"tags":{"name":"Café do Rio","amenity":"cafe","addr:city":"Belém"}},
          {"type":"node","id":3,"lat":-1.4,"lon":-48.4,"tags":{"name":"Outro município","amenity":"restaurant","addr:city":"Ananindeua"}}
        ]}
        """;

    [Fact]
    public void Parser_MapsStableIdsCoordinatesContactsAndRejectsOtherMunicipality()
    {
        var places = OpenStreetMapDiscoveryClient.Parse(Payload, "Belém");
        Assert.Equal(2, places.Count);
        Assert.Equal("osm:node:1", places[0].ExternalId);
        Assert.Equal("Pizzaria", places[0].Category);
        Assert.Equal("Rua da Feira, 10", places[0].Address);
        Assert.Equal("(91) 3333-4444", places[0].Phone);
        Assert.Null(places[0].Website);
        Assert.Equal(-1.44, places[1].Latitude);
        Assert.Equal("https://www.openstreetmap.org/way/2", places[1].GoogleMapsUri);
    }

    [Fact]
    public async Task CitySnapshotIsSharedAcrossCategories_WithoutRepeatedExternalRequests()
    {
        var handler = new Handler(Payload);
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);
        var pizza = await client.SearchAsync("pizzaria em Belém, Pará", "Belém", default);
        var cafe = await client.SearchAsync("cafeteria em Belém, Pará", "Belém", default);
        Assert.Single(pizza.Places); Assert.Single(cafe.Places);
        Assert.Equal("Cafeteria", cafe.Places[0].Category);
        Assert.Equal(1, handler.Requests);
        Assert.NotNull(cache.Snapshot);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync("restaurant", "São Paulo", default));
    }

    [Fact]
    public async Task PartialOverpassResponseIsNeverCachedAsSuccessfulDiscovery()
    {
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(new Handler("""{"remark":"runtime error","elements":[]}""")), cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchAsync("regional", "Belém", default));
        Assert.Null(cache.Snapshot);
    }

    [Theory]
    [InlineData("osm:node:123")]
    [InlineData("crawler:abc")]
    [InlineData("suggestion:42")]
    [InlineData("cnpj:11222333000181")]
    public async Task ExternalSourceIdsAreNeverSentToGoogleDetails(string externalId)
    {
        var handler = new Handler("{}");
        var google = new BelemRestaurantes.Infrastructure.Google.GooglePlacesClient(new HttpClient(handler),
            new BelemRestaurantes.Infrastructure.Google.GooglePlacesOptions { ApiKey = "test-key" },new Clock(),
            NullLogger<BelemRestaurantes.Infrastructure.Google.GooglePlacesClient>.Instance);
        Assert.Null(await google.GetDetailsAsync(externalId, default));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData(double.NaN, -48, false)]
    [InlineData(-1.45, double.PositiveInfinity, false)]
    [InlineData(91, 0, false)]
    [InlineData(-1.45, -48.49, true)]
    public void NearbyQuery_RejectsInvalidCoordinates(double latitude, double longitude, bool expected)
    {
        var query = new RestaurantSearchQuery(Latitude: latitude, Longitude: longitude, Sort: "nearby", RadiusKm: 900);
        Assert.Equal(expected, query.HasLocation);
        Assert.Equal(100, query.SafeRadiusKm);
        Assert.Equal(expected ? "nearby" : "name", query.SafeSort);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Cache : IOpenDataCache
    {
        public OpenDataSnapshot? Snapshot { get; private set; }
        public Task<OpenDataSnapshot?> GetAsync(string city, CancellationToken ct) => Task.FromResult(Snapshot);
        public Task SaveAsync(OpenDataSnapshot snapshot, CancellationToken ct) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<IReadOnlyList<OpenDataSnapshot>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenDataSnapshot>>(Snapshot is null ? [] : [Snapshot]);
    }
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
