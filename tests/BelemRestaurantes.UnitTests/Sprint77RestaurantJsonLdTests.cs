using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
namespace BelemRestaurantes.UnitTests;
// Sprint 77: a ficha pública ganhou compartilhamento de verdade (og:url, og:image absoluto e
// JSON-LD schema.org/Restaurant). O builder é função pura — aqui se prova a regra de honestidade:
// cada chave só existe quando a ficha realmente tem o dado correspondente, e a origem das URLs
// é sempre HTTPS e absoluta (mesma regra do sitemap da Sprint 76).
public sealed class Sprint77RestaurantJsonLdTests
{
    private const string Origin = "https://belemrestaurantes.com.br";

    [Fact]
    public void BuildWithFullCardEmitsEveryKeyThatActuallyExists()
    {
        var restaurant = new RestaurantView(42, "Casa de Teste", "Belém", "Cidade Velha", "Restaurante",
            "Rua dos Tempestades 90", "https://casadeteste.example.org", null, null, null, null,
            "/media/photo/casa.webp", new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero))
        {
            Latitude = -1.4550013,
            Longitude = -48.4931780,
            Phone = "93 3234-5678"
        };
        var schema = RestaurantJsonLd.Build(restaurant, Origin);

        Assert.Equal("https://schema.org", schema["@context"]);
        Assert.Equal("Restaurant", schema["@type"]);
        Assert.Equal("Casa de Teste", schema["name"]);
        Assert.Equal(Origin + "/estabelecimento/42", schema["url"]);
        Assert.Equal(Origin + "/media/photo/casa.webp", schema["image"]);
        Assert.Equal("93 3234-5678", schema["telephone"]);
        Assert.Equal("https://casadeteste.example.org", schema["sameAs"]);
        var address = (Dictionary<string, object?>)schema["address"]!;
        Assert.Equal("PostalAddress", address["@type"]);
        Assert.Equal("Rua dos Tempestades 90", address["streetAddress"]);
        Assert.Equal("Belém", address["addressLocality"]);
        var geo = (Dictionary<string, object?>)schema["geo"]!;
        // Coordenada em texto com ponto decimal (invariante): buscador não chora vírgula de pt-BR.
        Assert.Equal("-1.4550013", geo["latitude"]);
        Assert.Equal("-48.493178", geo["longitude"]);
    }

    [Fact]
    public void BuildWithBareCardInventsNothing()
    {
        var restaurant = new RestaurantView(7, "Cantina Sem Foto", "Santarém", null, null, null,
            null, null, null, null, null, null, default);
        var schema = RestaurantJsonLd.Build(restaurant, Origin);

        Assert.Equal(4, schema.Count); // só contexto, tipo, nome e url
        Assert.Equal(Origin + "/estabelecimento/7", schema["url"]);
        Assert.False(schema.ContainsKey("image"));
        Assert.False(schema.ContainsKey("address"));
        Assert.False(schema.ContainsKey("telephone"));
        Assert.False(schema.ContainsKey("sameAs"));
        Assert.False(schema.ContainsKey("geo"));
    }

    [Fact]
    public void GeoRequiresBothCoordinatesInsideValidRange()
    {
        static RestaurantView Card(double? lat = null, double? lng = null) =>
            new(9, "Ponto de Prova", "Belém", null, null, null, null, null, null, null, null, null, default)
            { Latitude = lat, Longitude = lng };
        Assert.False(RestaurantJsonLd.Build(Card(lat: -1.455), Origin).ContainsKey("geo")); // só latitude não conta
        Assert.False(RestaurantJsonLd.Build(Card(lat: 95, lng: -48.5), Origin).ContainsKey("geo")); // fora do mundo não é coordenada
        Assert.True(RestaurantJsonLd.Build(Card(lat: -1.455, lng: -48.5), Origin).ContainsKey("geo"));
    }

    [Fact]
    public void AbsolutePhotoUrlAcceptsTheTwoPublicShapesAndRejectsTheRest()
    {
        Assert.Equal(Origin + "/media/photo/a.webp", RestaurantJsonLd.AbsolutePhotoUrl("/media/photo/a.webp", Origin));
        Assert.Equal("https://cdn.fonte.org/foto.jpg", RestaurantJsonLd.AbsolutePhotoUrl("https://cdn.fonte.org/foto.jpg", Origin));
        Assert.Null(RestaurantJsonLd.AbsolutePhotoUrl(null, Origin));
        Assert.Null(RestaurantJsonLd.AbsolutePhotoUrl("   ", Origin));
        Assert.Null(RestaurantJsonLd.AbsolutePhotoUrl("foto.jpg", Origin)); // caminho relativo sem /media/ não é foto pública conhecida
        Assert.Null(RestaurantJsonLd.AbsolutePhotoUrl("http://fontena.com/foto.jpg", Origin)); // HTTP não entra em og:image daqui
    }

    [Fact]
    public void PageUrlIsAbsoluteOnConfiguredOrigin()
    {
        Assert.Equal(Origin + "/estabelecimento/1234567", RestaurantJsonLd.PageUrl(1234567, Origin));
        Assert.Throws<ArgumentException>(() => SitemapBuilder.ResolvePublicOrigin("http://inseguro.example.org"));
    }

    [Fact]
    public void SerializedGraphIsValidJsonWithRestaurantType()
    {
        var restaurant = new RestaurantView(1, "Churrascaria Exemplo", "Belém", null, null, null,
            null, null, null, null, null, null, default);
        var json = JsonSerializer.Serialize(RestaurantJsonLd.Build(restaurant, Origin));
        using var doc = JsonDocument.Parse(json); // quebra aqui se o grafo não serializar limpo
        Assert.Equal("Restaurant", doc.RootElement.GetProperty("@type").GetString());
        Assert.Equal(Origin + "/estabelecimento/1", doc.RootElement.GetProperty("url").GetString());
    }
}
