using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 64 — "segundo olhar": o logradouro usado quando o endereço completo não devolve
/// candidato nenhum, e a estabilidade dos motivos que a busca em duas etapas produz na fila.</summary>
public sealed class Sprint64StreetSecondLookTests
{
    [Theory]
    // Endereço que já é só o logradouro: repetir a busca mudaria nada.
    [InlineData("RUA DOIS IRMAOS")]
    [InlineData("TRAVESSA ICOARACI")]
    // Numeral no meio do nome não é número: o logradouro inteiro se mantém.
    [InlineData("RUA 14 DE AGOSTO")]
    // "Sem número" curto demais para carregar informação.
    [InlineData("SN")]
    // Só espaços / vazio.
    [InlineData("")]
    // Espaçamento redundante: após trim é o mesmo que o original.
    [InlineData("  RUA X  ")]
    public void SecondLookIsSkippedWhenItWouldNotChangeTheQuery(string? address) =>
        Assert.Null(GeocodingRules.StreetForSecondLook(address));

    [Fact]
    public void NullAddressSkipsSecondLook() => Assert.Null(GeocodingRules.StreetForSecondLook(null));

    [Theory]
    // Número em parte separada some da busca.
    [InlineData("RUA X, 15", "RUA X")]
    // Caso real: avenida com nome abreviado + número.
    [InlineData("AVENIDA NSA SRA DE NAZARE, 881", "AVENIDA NSA SRA DE NAZARE")]
    // Número como último token do logradouro é removido (mesma convenção de SplitStreetAddress).
    [InlineData("RUA X 12", "RUA X")]
    [InlineData("AVENIDA V S 10, SN", "AVENIDA V S")]
    // Rodovia: só a primeira parte separada por vírgula segue para a busca.
    [InlineData("RODOVIA BR 163, KM 121, M/D, S/N", "RODOVIA BR")]
    // Nome com numeral interno preservado; número vem em parte separada.
    [InlineData("RUA 14 DE AGOSTO, 5", "RUA 14 DE AGOSTO")]
    // Espaçamentos internos na numeração/complemento não vazam para o logradouro.
    [InlineData("RUA X,   15", "RUA X")]
    public void SecondLookReturnsTheStreetPart(string address, string expected) =>
        Assert.Equal(expected, GeocodingRules.StreetForSecondLook(address));

    [Fact]
    public void EmptyResultReasonsStayStableAcrossBothLookupStages()
    {
        Assert.Equal("no_candidates", GeocodingRules.NoCandidates);
        Assert.Equal("no_candidates_street", GeocodingRules.NoCandidatesStreet);
        var decision = GeocodingRules.Evaluate("RUA X, 15", "Belém", []);
        Assert.False(decision.Accepted);
        Assert.Equal(GeocodingRules.NoCandidates, decision.Reason);
    }

    [Fact]
    public void StreetLookupPoolStillPassesThroughTheStrictEvaluation()
    {
        // O pool do segundo olhar é avaliado contra o endereço ORIGINAL: ficha com número exige número igual.
        var decision = GeocodingRules.Evaluate("AVENIDA NSA SRA DE NAZARE, 881", "Belém",
            [new NominatimCandidate(-1.47, -48.48, "road", 99, "Avenida Nossa Senhora de Nazaré", null, "Belém", "Pará")]);
        Assert.False(decision.Accepted);
        Assert.Equal(GeocodingRules.UnsupportedType, decision.Reason);
    }
}
