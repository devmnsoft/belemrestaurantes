using BelemRestaurantes.Infrastructure.Crawler;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint58GeocodingTests
{
    private static NominatimCandidate C(string type, string? house = null, string? state = "Pará", double lat = -1.45, double lng = -48.5, string? road = "Travessa Três de Maio", string? city = "Belém", long id = 1) =>
        new(lat, lng, type, id, road, house, city, state);

    [Fact] public void StreetSplitsTrailingNumberAndCommaNumber()
    {
        var comma = GeocodingRules.SplitStreetAddress("Travessa Três de Maio, 1680, São Brás");
        Assert.Equal("Travessa Três de Maio", comma.Street);
        Assert.Equal(1680, comma.HouseNumber);

        var trailing = GeocodingRules.SplitStreetAddress("Avenida Gentil Bittencourt 1680, São Brás");
        Assert.Equal(1680, trailing.HouseNumber);

        var named = GeocodingRules.SplitStreetAddress("Rua 14 de Agosto");
        Assert.Equal("Rua 14 de Agosto", named.Street);
        Assert.Null(named.HouseNumber);
    }

    [Fact] public void RooftopObjectInsideParaWithMatchingHouseIsAccepted()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680, São Brás", "Belém", [C("house", house: "1680")]);
        Assert.True(d.Accepted);
        Assert.Equal("rooftop", d.Precision);
        Assert.Equal(-1.45, d.Latitude);
        Assert.Equal(1L, d.PlaceId);
    }

    [Fact] public void AddressLevelWithoutFichaHouseIsApproximate()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio", "Belém", [C("address")]);
        Assert.True(d.Accepted);
        Assert.Equal("approximate", d.Precision);
    }

    [Fact] public void CandidateFromAnotherStateNeverPlacesTheFicha()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Belém", [C("house", house: "1680", state: "Ceará", lat: -3.7)]);
        Assert.False(d.Accepted);
        Assert.Equal("outside_state", d.Reason);
    }

    [Fact] public void HouseNumberMismatchIsRejectedWithoutInventingThePlace()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Belém", [C("house", house: "1682")]);
        Assert.False(d.Accepted);
        Assert.Equal("house_number_mismatch", d.Reason);
    }

    [Fact] public void TwoDifferentObjectsPassingAreAmbiguous()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Belém",
            [C("house", house: "1680", id: 1), C("building", house: "1680", id: 2, lng: -48.51)]);
        Assert.False(d.Accepted);
        Assert.Equal("ambiguous", d.Reason);
    }

    [Fact] public void DuplicateOfTheSameObjectIsNotAmbiguous()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Belém",
            [C("house", house: "1680", id: 7), C("house", house: "1680", id: 7)]);
        Assert.True(d.Accepted);
        Assert.Equal(7L, d.PlaceId);
    }

    [Fact] public void RoadLevelTypeDoesNotPlaceTheFicha()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio", "Belém", [C("road")]);
        Assert.False(d.Accepted);
        Assert.Equal("unsupported_type", d.Reason);
    }

    [Fact] public void StreetMismatchIsRejected()
    {
        var d = GeocodingRules.Evaluate("Travessa do Comércio, 100", "Belém", [C("house", house: "100", road: "Travessa Três de Maio")]);
        Assert.False(d.Accepted);
        Assert.Equal("street_mismatch", d.Reason);
    }

    [Fact] public void WordNumberInFichaMatchesDigitInOsRoad()
    {
        // Ficha diz "Travessa Três de Maio"; o OSM chama a mesma rua de "Travessa 3 de Maio".
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Belém", [C("house", house: "1680", road: "Travessa 3 de Maio")]);
        Assert.True(d.Accepted);
        Assert.Equal("rooftop", d.Precision);
    }

    [Fact] public void DifferentDayNamesAreStillDifferentStreets()
    {
        // "14" e "15" só valem como numeral verbal na palavra inteira ("quatorze"/"quinze"); dígitos distintos não conflitam.
        var d = GeocodingRules.Evaluate("Rua 14 de Agosto, 200", "Belém", [C("house", house: "200", road: "Rua 15 de Agosto")]);
        Assert.False(d.Accepted);
        Assert.Equal("street_mismatch", d.Reason);
    }

    [Fact] public void CityMismatchIsRejected()
    {
        var d = GeocodingRules.Evaluate("Travessa Três de Maio, 1680", "Ananindeua", [C("house", house: "1680", city: "Belém")]);
        Assert.False(d.Accepted);
        Assert.Equal("city_mismatch", d.Reason);
    }

    [Fact] public void CoordinatesOutsideTheStateBoxAreRejected()
    {
        // Estado declarado como Pará mas coordenada ao norte do estado (Amapá): a rejeição é por coordenada.
        var d = GeocodingRules.Evaluate("Travessa Três de Maio", "Belém", [C("house", lat: 1.2, city: "Belém")]);
        Assert.False(d.Accepted);
        Assert.Equal("coordinates_out_of_range", d.Reason);
    }

    [Fact] public void EmptyResultIsRejected()
        => Assert.Equal("no_candidates", GeocodingRules.Evaluate("Travessa Três de Maio", "Belém", Array.Empty<NominatimCandidate>()).Reason);

    [Fact] public void NominatimParserReadsCandidatesAndIgnoresInvalidOnes()
    {
        // Formato real do Nominatim (jsonv2 + addressdetails): o número mora em "house_number".
        const string json = """
            [
             {"place_id":11,"lat":"-1.4637912","lon":"-48.4939541","type":"house","addresstype":["house","suburb","city","state","country"],"address":{"house_number":"1680","road":"Travessa 3 de Maio","city":"Belém","state":"Pará"}},
             {"place_id":12,"lat":"-1.46","lon":"-48.49","type":"building","address":{"house":"77","road":"Rua do Comércio","city":"Belém","state":"Pará"}},
             {"place_id":22,"type":"city"},
             {"place_id":33,"lat":"abc","lon":"-48.4","type":"cafe"}
            ]
            """;
        var c = NominatimGeocodingSource.ParseCandidates(json);
        Assert.Equal(2, c.Count);
        Assert.Equal(11L, c[0].PlaceId);
        Assert.Equal("1680", c[0].HouseNumber);
        Assert.Equal("Belém", c[0].City);
        Assert.Equal("Pará", c[0].State);
        Assert.Equal("77", c[1].HouseNumber); // chave antiga "house" continua aceita como fallback
    }

    [Fact] public void ParserIgnoresBrokenJson()
    {
        Assert.Empty(NominatimGeocodingSource.ParseCandidates("{não é um array"));
        Assert.Empty(NominatimGeocodingSource.ParseCandidates(""));
    }

    [Fact] public void CollectionQuerySendsAddressCityAndStateBoundedToTwoHundredChars()
    {
        Assert.Equal("Travessa Três de Maio, 1680, São Brás, Belém, Pará, Brasil",
            NominatimGeocodingSource.BuildQuery("Travessa Três de Maio, 1680, São Brás", "Belém"));
        var longAddress = new string('a', 500);
        Assert.Equal(200, NominatimGeocodingSource.BuildQuery(longAddress, "Belém").Length);
    }
}
