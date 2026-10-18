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
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task TransientFailureIsRetriedBeforeSavingValidSnapshot(HttpStatusCode status)
    {
        var handler = new SequenceHandler(attempt => new HttpResponseMessage(attempt == 1 ? status : HttpStatusCode.OK)
            { Content = new StringContent(Payload) });
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        var result = await client.SearchAsync("regional", "Bannach", default);

        Assert.Equal(2, handler.Requests);
        Assert.NotEmpty(result.Places);
        Assert.Equal(Payload, cache.Snapshot?.Payload);
    }

    [Fact]
    public async Task ExhaustedRetriesPreserveExistingSnapshotAndOriginalHttpStatus()
    {
        var handler = new SequenceHandler(attempt => new HttpResponseMessage(attempt <= 3 ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new StringContent(Payload) });
        var cache = new Cache();
        var original = new OpenDataSnapshot("Belém", Payload, DateTimeOffset.UtcNow.AddDays(-2));
        await cache.SaveAsync(original, default);
        var clock = new MutableClock();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, clock, NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        var error = await Assert.ThrowsAsync<DiscoveryProviderUnavailableException>(() => client.SearchAsync("regional", "Belém", default));

        Assert.Equal(HttpStatusCode.BadGateway, Assert.IsType<HttpRequestException>(error.InnerException).StatusCode);
        Assert.Equal(3, handler.Requests);
        Assert.Same(original, cache.Snapshot);
        var paused = await Assert.ThrowsAsync<DiscoveryProviderUnavailableException>(() => client.SearchAsync("regional", "Jacundá", default));
        Assert.Equal(error.RetryAt, paused.RetryAt);
        Assert.Equal(3, handler.Requests);
        clock.UtcNow = error.RetryAt.AddSeconds(1);
        await client.SearchAsync("regional", "Belém", default);
        Assert.Equal(4, handler.Requests);
        Assert.NotSame(original, cache.Snapshot);
    }

    [Fact]
    public async Task PermanentHttpFailureIsNotRetriedOrCached()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("regional", "Belém", default));

        Assert.Equal(1, handler.Requests);
        Assert.Null(cache.Snapshot);
    }

    [Fact]
    public async Task RetryAfterWaitCanBeCancelledAndReleasesCityGate()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new SequenceHandler(attempt =>
        {
            var response = new HttpResponseMessage(attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
                { Content = new StringContent(Payload) };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            if (attempt == 1) cancellation.CancelAfter(TimeSpan.FromSeconds(4));
            return response;
        });
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync("regional", "Belém", cancellation.Token));
        Assert.Equal(1, handler.Requests);
        Assert.Null(cache.Snapshot);
        await client.SearchAsync("regional", "Belém", default);
        Assert.Equal(2, handler.Requests);
    }

    private sealed class SequenceHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(++Requests));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongRetryAfterDefersDiscoveryAndPausesOtherCitiesWithoutNetworkRequests(bool dateHeader)
    {
        var clock = new MutableClock();
        var retryAt = clock.UtcNow.AddMinutes(10);
        var handler = new SequenceHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = dateHeader
                ? new System.Net.Http.Headers.RetryConditionHeaderValue(retryAt)
                : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        var cache = new Cache();
        var client = new OpenStreetMapDiscoveryClient(new HttpClient(handler), cache, clock, NullLogger<OpenStreetMapDiscoveryClient>.Instance);
        var orchestrator = new BelemRestaurantes.Application.Services.DiscoveryOrchestrator(client,
            null!, null!, null!, clock, NullLogger<BelemRestaurantes.Application.Services.DiscoveryOrchestrator>.Instance);

        var result = await orchestrator.RunTargetAsync(new DiscoveryWorkItem(1, 1, "Maracanã", "regional", 0), default);
        var other = await orchestrator.RunTargetAsync(new DiscoveryWorkItem(2, 2, "Jacundá", "regional", 0), default);

        Assert.Equal(1, result.JobsFailed);
        Assert.Equal(retryAt, result.ProviderRetryAt);
        Assert.Equal(retryAt, other.ProviderRetryAt);
        Assert.Equal(1, handler.Requests);
        Assert.Null(cache.Snapshot);
        await cache.SaveAsync(new("Belém", Payload, clock.UtcNow), default);
        Assert.NotEmpty((await client.SearchAsync("regional", "Belém", default)).Places);
        Assert.Equal(1, handler.Requests); // Cache válido continua disponível durante a pausa.
    }

    [Fact]
    public async Task HttpTimeoutRecoversUsingAlternativeServer()
    {
        var handler = new TimeoutHandler(recover: true);
        var cache = new Cache();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var client = new OpenStreetMapDiscoveryClient(http, cache, new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        var result = await client.SearchAsync("regional", "Itupiranga", default);

        Assert.NotEmpty(result.Places);
        Assert.Equal(new[] { OpenStreetMapDiscoveryClient.DefaultEndpoint, OpenStreetMapDiscoveryClient.DefaultFallbackEndpoint }, handler.Endpoints);
        Assert.NotNull(cache.Snapshot);
    }

    [Fact]
    public async Task ExhaustedHttpTimeoutsAreRecordedAsFailedDiscoveryWithoutReplacingSnapshot()
    {
        var handler = new TimeoutHandler(recover: false);
        var cache = new Cache();
        var original = new OpenDataSnapshot("Itupiranga", Payload, DateTimeOffset.UtcNow.AddDays(-2));
        await cache.SaveAsync(original, default);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var clock = new Clock();
        var client = new OpenStreetMapDiscoveryClient(http, cache, clock, NullLogger<OpenStreetMapDiscoveryClient>.Instance);
        var orchestrator = new BelemRestaurantes.Application.Services.DiscoveryOrchestrator(client,
            null!, null!, null!, clock, NullLogger<BelemRestaurantes.Application.Services.DiscoveryOrchestrator>.Instance);

        var metrics = await orchestrator.RunTargetAsync(new DiscoveryWorkItem(1, 1, "Itupiranga", "regional", 0), default);

        Assert.Equal(1, metrics.JobsFailed);
        Assert.Equal(0, metrics.PlacesDiscovered);
        Assert.Contains("3 tentativas", metrics.FailureReason);
        Assert.NotNull(metrics.ProviderRetryAt);
        Assert.Equal(3, handler.Endpoints.Count);
        Assert.Same(original, cache.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationDoesNotContactFallbackOrBecomeTimeout()
    {
        var handler = new TimeoutHandler(recover: false);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var client = new OpenStreetMapDiscoveryClient(http, new Cache(), new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync("regional", "Itupiranga", cancellation.Token));

        Assert.Single(handler.Endpoints);
    }

    [Fact]
    public async Task CustomServerDoesNotUsePublicFallbackByDefault()
    {
        var handler = new TimeoutHandler(recover: true);
        var endpoint = new Uri("https://overpass.example.test/api/interpreter");
        using var http = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromMilliseconds(100) };
        var client = new OpenStreetMapDiscoveryClient(http, new Cache(), new Clock(), NullLogger<OpenStreetMapDiscoveryClient>.Instance);

        await client.SearchAsync("regional", "Itupiranga", default);

        Assert.Equal(2, handler.Endpoints.Count);
        Assert.All(handler.Endpoints, actual => Assert.Equal(endpoint, actual));
    }

    private sealed class TimeoutHandler(bool recover) : HttpMessageHandler
    {
        public List<Uri> Endpoints { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Endpoints.Add(request.RequestUri!);
            if (!recover || Endpoints.Count == 1) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Payload) };
        }
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
    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
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
