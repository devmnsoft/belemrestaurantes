using BelemRestaurantes.Application.Services;
using Xunit;

namespace BelemRestaurantes.UnitTests;

// Sprint 80 — regras puras dos alternates hreflang. A honestidade é o contrato: somente idiomas
// com tradução própria verificada ganham endereço; x-default aponta para o idioma de registro;
// a página em outra língua carrega o sufixo ?lang=xx uma única vez.
public sealed class Sprint80HreflangBuilderTests
{
    [Theory]
    [InlineData("pt-BR", "/cidade/santarem", "https://exemplo.org/cidade/santarem")]
    [InlineData("en", "/cidade/santarem", "https://exemplo.org/cidade/santarem?lang=en")]
    [InlineData("es", "/cidade/santarem", "https://exemplo.org/cidade/santarem?lang=es")]
    public void AlternateUrlUsesTheBarePageOnlyForTheDefaultLanguage(string culture, string path, string expected)
    {
        Assert.Equal(expected, HreflangBuilder.AlternateUrl("https://exemplo.org", path, culture));
    }

    [Fact]
    public void AlternateUrlToleratesTrailingSlashOnTheOrigin()
    {
        Assert.Equal("https://exemplo.org/guia?lang=en", HreflangBuilder.AlternateUrl("https://exemplo.org/", "/guia", "en"));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    public void LanguagesWithOnlyTheEnglishFallbackGetNoAddress(string culture)
    {
        // Declarar tradução onde não há seria inventar: esses resx são o fallback inglês que o site informa.
        Assert.Throws<ArgumentException>(() => HreflangBuilder.AlternateUrl("https://exemplo.org", "/guia", culture));
    }

    [Fact]
    public void AlternateUrlStillDemandsHttps()
    {
        Assert.Throws<ArgumentException>(() => HreflangBuilder.AlternateUrl("http://exemplo.org", "/guia", "en"));
    }

    [Theory]
    [InlineData("cidade/santarem")]
    [InlineData("/guia?lang=en")]
    [InlineData("")]
    public void AlternatePathMustBeAbsoluteAndFreeOfQueries(string path)
    {
        Assert.Throws<ArgumentException>(() => HreflangBuilder.AlternateUrl("https://exemplo.org", path, "pt-BR"));
    }

    [Fact]
    public void AlternatesListsEveryTranslatedLanguagePlusXDefault()
    {
        var alternates = HreflangBuilder.Alternates("https://exemplo.org", "/cidade/belem");

        var tags = alternates.Select(a => a.Tag).ToArray();
        Assert.Equal(new[] { "pt-br", "en", "es", "x-default" }, tags);

        Assert.Equal("https://exemplo.org/cidade/belem", alternates[0].Url);
        Assert.Equal("https://exemplo.org/cidade/belem?lang=en", alternates[1].Url);
        Assert.Equal("https://exemplo.org/cidade/belem?lang=es", alternates[2].Url);
        // x-default é a página de registro: idêntica à versão pt-BR, sem parâmetro.
        Assert.Equal(alternates[0].Url, alternates[3].Url);
    }

    [Theory]
    [InlineData("https://exemplo.org/cidade/belem", "pt-BR", "https://exemplo.org/cidade/belem")]
    [InlineData("https://exemplo.org/cidade/belem", "en", "https://exemplo.org/cidade/belem?lang=en")]
    [InlineData("https://exemplo.org/cidade/belem", "es", "https://exemplo.org/cidade/belem?lang=es")]
    [InlineData("https://exemplo.org/cidade/belem?lang=en", "en", "https://exemplo.org/cidade/belem?lang=en")]
    public void OgUrlPointsToItsOwnLanguageExactlyOnce(string ogUrl, string culture, string expected)
    {
        Assert.Equal(expected, HreflangBuilder.OgUrlForCulture(ogUrl, culture));
    }

    [Fact]
    public void OgUrlForCultureRejectsAnEmptyCanonical()
    {
        Assert.Throws<ArgumentException>(() => HreflangBuilder.OgUrlForCulture(" ", "en"));
    }
}
