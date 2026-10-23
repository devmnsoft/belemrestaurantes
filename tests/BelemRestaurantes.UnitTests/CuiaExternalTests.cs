using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;

namespace BelemRestaurantes.UnitTests;

public sealed class CuiaExternalTests
{
    private static readonly WebSearchSource[] Sources = [new("Casa do Rio em Belém", "https://example.org/casa-do-rio",
        "A Casa do Rio fica no bairro da Campina, em Belém. Esta fonte descreve a cozinha regional.", DateTimeOffset.UtcNow)];

    [Fact]
    public void ExternalSuggestionRequiresCitedNameAndUsesOnlySourceEvidence()
    {
        const string raw = """{"answer":"Veja uma opção externa [aqui](https://inventado.example/).","externalSuggestions":[{"name":"Casa do Rio","city":"Belém","neighborhood":"Campina","evidence":"Esta fonte descreve a cozinha regional.","sourceNumbers":[1]}]}""";
        Assert.True(CuiaAnswerRules.TryRead(raw, Sources, [], out var answer, out var places));
        Assert.DoesNotContain("https://", answer);
        var place = Assert.Single(places); Assert.Equal("Casa do Rio", place.Name); Assert.Equal("Belém", place.City);
        Assert.Equal("Campina", place.Neighborhood); Assert.Equal("Esta fonte descreve a cozinha regional.", place.Evidence);
    }

    [Theory]
    [InlineData("Lugar Inventado", 1)]
    [InlineData("Casa do Rio", 0)]
    [InlineData("Casa do Rio", 99)]
    public void UnsupportedNamesOrSourcesNeverBecomeExternalCards(string name, int source)
    {
        var raw = System.Text.Json.JsonSerializer.Serialize(new { answer = "Resposta contextual.", externalSuggestions = new[] { new { name, sourceNumbers = new[] { source } } } });
        Assert.True(CuiaAnswerRules.TryRead(raw, Sources, [], out _, out var places)); Assert.Empty(places);
    }

    [Fact]
    public void InventedPriceAndLocationAreDiscarded()
    {
        const string raw = """{"answer":"Consulte a fonte.","externalSuggestions":[{"name":"Casa do Rio","city":"Santarém","neighborhood":"Nazaré","evidence":"Almoço a R$ 20 e aberto agora.","sourceNumbers":[1]}]}""";
        Assert.True(CuiaAnswerRules.TryRead(raw, Sources, [], out _, out var places));
        var place=Assert.Single(places); Assert.Null(place.City); Assert.Null(place.Neighborhood);
        Assert.DoesNotContain("20",place.Evidence); Assert.Contains("confirme",place.Evidence);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"answer\":\"\"}")]
    public void InvalidProviderRepliesAreNotAccepted(string raw) => Assert.False(CuiaAnswerRules.TryRead(raw,Sources,[],out _,out _));

    [Theory]
    [InlineData("Escreva programação em Python para mim", true)]
    [InlineData("Quero comprar bitcoin", true)]
    [InlineData("Quero uma receita vegetariana em Belém", false)]
    [InlineData("Você conhece Remanso do Bosque?", false)]
    [InlineData("Como funciona o site?", false)]
    public void ScopeAllowsCulinaryQueriesAndUnrecognizedBusinessNames(string prompt,bool outside) => Assert.Equal(outside,CuiaAnswerRules.ClearlyOutsideScope(prompt));
}
