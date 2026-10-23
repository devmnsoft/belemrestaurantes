using BelemRestaurantes.Application.Services;
namespace BelemRestaurantes.UnitTests;
public sealed class Sprint53SearchTests
{
    [Fact]public void TopicsAreAccentedDeduplicatedAndWholeWords(){Assert.Equal(new[]{"tacacá","restaurante","belém"},SearchTopics.Extract("Tacaca restaurante restaurantes em BELEM tacacá"));Assert.Empty(SearchTopics.Extract("restaurantesco para comercialização"));}
    [Theory][InlineData("meu cpf 123.456.789-00 restaurantes")][InlineData("restaurante email pessoa@example.org")][InlineData("senha de restaurante")][InlineData("contato 91999999999 Belém")]
    public void SensitiveQueriesAreNotStoredOrCounted(string query){Assert.True(SearchTopics.IsPrivate(query));Assert.Empty(SearchTopics.Extract(query));}
    [Fact]public void RankingPrefersSpecificEvidenceAndDeduplicatesTrackingUrls()
    {
        var time=DateTimeOffset.UtcNow;
        var rows=SearchRanking.Merge("Tacacá Belém",[new("Pará","https://example.org/generic","Culinária",time),new("Tacacá Belém","https://example.org/tacaca?utm_source=one","Fonte",time),new("Tacacá Belém","https://example.org/tacaca","Fonte",time)]);
        Assert.Single(rows);Assert.Equal("Tacacá Belém",rows[0].Title);
    }
    [Fact]public void IdentifiedRestaurantExcludesAmbiguousEncyclopediaTopic()
    {
        var tide=new BelemRestaurantes.Application.Abstractions.WebSearchSource("Maré · Wikipédia","https://pt.wikipedia.org/?curid=1","Fenômeno natural",DateTimeOffset.UtcNow){SourceKind="encyclopedia"};
        Assert.Empty(SearchRanking.ForKnownPlace("Na Maré Belém",["Na Maré"],[tide]));Assert.Single(SearchRanking.ForKnownPlace("marés",[],[tide]));
    }
    [Fact]public void CacheNormalizationIsStable(){Assert.Equal(SearchTopics.Normalize("  TACACÁ   Belém  "),SearchTopics.Normalize("tacaca belem"));}
}
