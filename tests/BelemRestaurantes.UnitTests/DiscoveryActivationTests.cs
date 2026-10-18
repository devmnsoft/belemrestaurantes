using System.Net;
using System.Security.Cryptography;
using System.Text;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Google;
using BelemRestaurantes.Infrastructure.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace BelemRestaurantes.UnitTests;

public sealed class DiscoveryActivationTests
{
    [Fact]
    public async Task GoogleSearch_MapsMunicipalityNeighborhoodAndPhone_IntoAcceptablePlace()
    {
        const string body = """
            {"places":[{"id":"place-one","displayName":{"text":"Tacacá da Feira"},
            "formattedAddress":"Rua da Feira, Belém - PA, Brasil","location":{"latitude":-1.45,"longitude":-48.49},
            "types":["restaurant"],"nationalPhoneNumber":"(91) 3333-4444","businessStatus":"CLOSED_TEMPORARILY",
            "addressComponents":[{"longText":"Campina","types":["sublocality_level_1"]},
              {"longText":"Belém","types":["administrative_area_level_2"]}]}]}
            """;
        var handler = new ResponseHandler(body);
        var client = Client(handler);
        var result = await client.SearchAsync("restaurante em Belém, Pará, Brasil", "Belém", default);
        var place = Assert.Single(result.Places);
        Assert.Equal("Belém", place.City);
        Assert.Equal("Campina", place.Neighborhood);
        Assert.Equal("(91) 3333-4444", place.Phone);
        Assert.Equal("temporarily_closed", place.BusinessStatus);
        Assert.True(PlaceValidator.Validate(place).IsAcceptable);
        Assert.Contains("places.addressComponents", handler.FieldMask);
        Assert.Equal("https://places.googleapis.com/v1/places:searchText", handler.Url);
    }

    [Fact]
    public async Task GoogleDetails_DoesNotSendSearchOnlyPaginationField()
    {
        var handler = new ResponseHandler("""{"id":"one","displayName":{"text":"Restaurante"},"types":["restaurant"]}""");
        var place = await Client(handler).GetDetailsAsync("one", default);
        Assert.NotNull(place);
        Assert.DoesNotContain("nextPageToken", handler.FieldMask);
        Assert.DoesNotContain("places.", handler.FieldMask);
    }

    [Fact]
    public void MigrationChecksums_AcceptOnlyLineEndingChanges()
    {
        var lf = new MigrationScript("001", "test", "SELECT 1;\nSELECT 2;\n");
        var crlf = lf with { Sql = lf.Sql.Replace("\n", "\r\n") };
        Assert.Equal(lf.ComputeChecksum(), crlf.ComputeChecksum());
        var legacyWindowsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(crlf.Sql)));
        Assert.True(lf.MatchesChecksum(legacyWindowsHash));
        Assert.False((lf with { Sql = "SELECT 3;\n" }).MatchesChecksum(lf.ComputeChecksum()));
    }

    [Fact]
    public void SouthernPara_PlacesAreNotRejectedByOldLatitudeLimit()
    {
        var place = new BelemRestaurantes.Application.Models.DiscoveredPlace("south", "Sabor do Araguaia", null,
            "Santana do Araguaia", "restaurante", "Centro", -9.33, -50.35, null, null, null, null, ["restaurant"]);
        Assert.True(PlaceValidator.Validate(place).IsAcceptable);
    }

    private static GooglePlacesClient Client(ResponseHandler handler) => new(new HttpClient(handler),
        new GooglePlacesOptions { ApiKey = "test-key" }, new TestClock(), NullLogger<GooglePlacesClient>.Instance);

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public string FieldMask { get; private set; } = "";
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            FieldMask = string.Join(",", request.Headers.GetValues("X-Goog-FieldMask"));
            Url = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
