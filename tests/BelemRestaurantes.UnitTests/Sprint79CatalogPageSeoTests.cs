using BelemRestaurantes.Application.Services;
using Xunit;

namespace BelemRestaurantes.UnitTests;

// Sprint 79 — regras puras do cartão de compartilhamento das páginas de catálogo.
// A honestidade é o contrato: contagem zero tem frase própria, plural segue o número real,
// números saem em cultura invariante e URL canônica nunca carrega consulta.
public sealed class Sprint79CatalogPageSeoTests
{
    [Theory]
    [InlineData("city", "Santarém", "Restaurantes em Santarém")]
    [InlineData("locality", "Praia do Atalaia", "Restaurantes em Praia do Atalaia")]
    [InlineData("category", "Loja de Alimentos", "Loja de Alimentos no Pará")]
    public void TitleUsesTheHonestShapePerKind(string kind, string name, string expected)
    {
        Assert.Equal(expected, CatalogPageSeo.Title(kind, name));
        Assert.Equal(expected + " | Belém Restaurantes", CatalogPageSeo.ShareTitle(kind, name));
    }

    [Theory]
    [InlineData("district")]
    [InlineData("")]
    public void UnknownKindFailsLoudly(string kind)
    {
        Assert.Throws<ArgumentException>(() => CatalogPageSeo.Title(kind, "Belém"));
        Assert.Throws<ArgumentException>(() => CatalogPageSeo.Description(3, kind, "Belém"));
    }

    [Theory]
    [InlineData(0, "city", "Belém", "Nenhum restaurante publicado em Belém até agora.")]
    [InlineData(1, "city", "Belém", "1 restaurante publicado em Belém.")]
    [InlineData(2924, "city", "Belém", "2924 restaurantes publicados em Belém.")]
    [InlineData(0, "locality", "Praia do Atalaia", "Nenhum restaurante publicado em Praia do Atalaia até agora.")]
    [InlineData(1, "category", "loja de alimentos", "1 estabelecimento publicado como loja de alimentos.")]
    [InlineData(1208, "category", "loja de alimentos", "1208 estabelecimentos publicados como loja de alimentos.")]
    public void DescriptionIsBuiltOnlyFromTheRealCount(int count, string kind, string name, string expected)
    {
        Assert.Equal(expected, CatalogPageSeo.Description(count, kind, name));
    }

    [Fact]
    public void DescriptionRejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CatalogPageSeo.Description(-1, "city", "Belém"));
    }

    [Fact]
    public void OgUrlJoinsTheConfiguredOriginWithTheExactPath()
    {
        Assert.Equal("https://belemrestaurantes.com.br/cidade/santarem",
            CatalogPageSeo.OgUrl("https://belemrestaurantes.com.br", "/cidade/santarem"));
    }

    [Fact]
    public void OgUrlStillDemandsHttpsAndStripsTrailingSlash()
    {
        Assert.Equal("https://exemplo.org/cidade/belem", CatalogPageSeo.OgUrl("https://exemplo.org/", "/cidade/belem"));
        Assert.Throws<ArgumentException>(() => CatalogPageSeo.OgUrl("http://exemplo.org", "/cidade/belem"));
    }

    [Fact]
    public void OgUrlNeverCarriesAQuery()
    {
        // Paginação e filtros não fazem parte da identidade canônica da página.
        Assert.Throws<ArgumentException>(() => CatalogPageSeo.OgUrl("https://exemplo.org", "/cidade/belem?page=2"));
        Assert.Throws<ArgumentException>(() => CatalogPageSeo.OgUrl("https://exemplo.org", "cidade/belem"));
    }
}
