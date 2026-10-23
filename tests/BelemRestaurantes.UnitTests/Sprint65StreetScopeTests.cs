using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 65 — escopo estável (logradouro, cidade) do cache negativo de rua.</summary>
public sealed class Sprint65StreetScopeTests
{
    [Fact]
    public void ScopeIgnoresHouseNumberAcrossFichasOfSameStreetAndCity()
    {
        var a = GeocodingRules.StreetScopeForLookup("RUA X 12", "Belém");
        var b = GeocodingRules.StreetScopeForLookup("RUA X 300", "Belém");
        var c = GeocodingRules.StreetScopeForLookup("RUA X, 15", "Belém");
        Assert.NotNull(a);
        Assert.Equal(("rua x", "belem"), (a!.StreetKey, a!.CityKey));
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void ScopeIsDifferentForDifferentCities()
    {
        var belem = GeocodingRules.StreetScopeForLookup("RUA X 12", "Belém");
        var santarem = GeocodingRules.StreetScopeForLookup("RUA X 12", "Santarém");
        Assert.NotNull(belem);
        Assert.NotNull(santarem);
        Assert.Equal("santarem", santarem!.CityKey);
        Assert.NotEqual(belem!, santarem);
    }

    [Theory]
    // Numeral final que é o número da ficha sai da chave de rua.
    [InlineData("RODOVIA BR 163, KM 121, M/D, S/N", "Altamira", "rodovia br", "altamira")]
    // "S" é palavra de ruído do normalizador — comportamento determinístico e consistente com StreetKey.
    [InlineData("AVENIDA V S 10, SN", "Parauapebas", "avenida v", "parauapebas")]
    public void ScopeUsesStreetFormWithoutTrailingNumber(string address, string city, string expectedStreet, string expectedCity)
    {
        var scope = GeocodingRules.StreetScopeForLookup(address, city);
        Assert.Equal((expectedStreet, expectedCity), (scope!.StreetKey, scope.CityKey));
    }

    [Theory]
    // Sem logradouro útil ou sem cidade não há escopo para cacher.
    [InlineData("SN", "Juruti")]
    [InlineData("", "Juruti")]
    [InlineData("RUA X", "")]
    [InlineData("RUA X", null)]
    public void ScopeIsNullWhenUseless(string? address, string? city) =>
        Assert.Null(GeocodingRules.StreetScopeForLookup(address, city));

    [Fact]
    public void NullAddressHasNoScope() => Assert.Null(GeocodingRules.StreetScopeForLookup(null, "Belém"));

    [Fact]
    public void StreetFormPreservesNameNumbersAndStripsOnlyTheFichaNumber()
    {
        Assert.Equal("RUA 14 DE AGOSTO", GeocodingRules.StreetForm("RUA 14 DE AGOSTO, 5"));
        Assert.Equal("RUA X", GeocodingRules.StreetForm("RUA X 12"));
        Assert.Equal("TRAVESSA ICOARACI", GeocodingRules.StreetForm("TRAVESSA ICOARACI"));
    }
}
