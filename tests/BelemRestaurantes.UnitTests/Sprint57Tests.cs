using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Crawler;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint57Tests
{
    [Fact] public void RecipeEvidenceKeepsAuthorIngredientsAndVideoWithoutInventingRestaurant()
    {
        const string html="""<script type="application/ld+json">{"@graph":[{"@type":"Recipe","name":"Hambúrguer de camarão","author":{"@type":"Person","name":"Autora"},"recipeIngredient":["450 g de camarão","1 limão"],"recipeInstructions":"Não republicar este texto","recipeYield":"4 porções","video":{"@type":"VideoObject","embedUrl":"https://www.youtube.com/embed/rJBEnyYwtbI?rel=0"}}]}</script>""";
        var r=Assert.Single(CulinaryStructuredParser.Parse(html,"https://example.org/receita","hambúrguer de camarão",DateTime.UtcNow));
        Assert.Equal("Autora",r.Author);Assert.Equal(2,r.Ingredients.Length);Assert.Null(r.PlaceName);Assert.Null(r.City);
        Assert.Equal("https://www.youtube.com/watch?v=rJBEnyYwtbI",r.VideoUrl);Assert.Contains("Não atribuída",r.Summary);
        Assert.DoesNotContain("Não republicar",System.Text.Json.JsonSerializer.Serialize(r));
    }
    [Fact] public void WrongRecipeAndIncompleteRecipeCannotBePublished()
    {
        const string html="""<script type="application/ld+json">[{"@type":"Recipe","name":"Patê de salsicha","recipeIngredient":["salsicha","maionese"]},{"@type":"Recipe","name":"Hambúrguer de camarão","recipeIngredient":[]}]</script>""";
        Assert.Empty(CulinaryStructuredParser.Parse(html,"https://example.org/redirect","hambúrguer de camarão",DateTime.UtcNow));
    }
    [Fact] public void InvalidJsonDoesNotTurnIntoARecipe()
        =>Assert.Empty(CulinaryStructuredParser.Parse("<script type='application/ld+json'>{oops}</script>","https://example.org/r","tacacá",DateTime.UtcNow));
    [Fact] public void MicrodataRecipeRequiresScopeAndIngredients()
    {
        const string html="""<main itemscope itemtype="http://schema.org/Recipe"><meta itemprop="name" content="Tacacá"><ul><li itemprop="recipeIngredient">2 litros de tucupi</li><li itemprop="recipeIngredient">Jambu</li></ul></main>""";
        var record=Assert.Single(CulinaryStructuredParser.Parse(html,"https://example.org/tacaca","tacacá",DateTime.UtcNow));
        Assert.Equal(2,record.Ingredients.Length);Assert.Null(record.PlaceName);
        Assert.Empty(CulinaryStructuredParser.Parse(html.Replace("schema.org/Recipe","schema.org/Article"),"https://example.org/tacaca","tacacá",DateTime.UtcNow));
    }
    [Fact] public void ProductPriceRequiresExplicitBrlAndMatchingIngredient()
    {
        const string html="""<script type="application/ld+json">{"@type":"Product","name":"Camarão limpo 500g","offers":{"@type":"Offer","price":"39.90","priceCurrency":"BRL"}}</script>""";
        var record=Assert.Single(CulinaryStructuredParser.Parse(html,"https://example.org/produto","camarão",DateTime.UtcNow));
        Assert.Equal(39.90m,record.Price);Assert.Equal("ingredient_offer",record.Kind);
        Assert.Empty(CulinaryStructuredParser.Parse(html.Replace("BRL","USD"),"https://example.org/produto","camarão",DateTime.UtcNow));
        Assert.Empty(CulinaryStructuredParser.Parse(html.Replace("39.90","39,90"),"https://example.org/produto","camarão",DateTime.UtcNow));
    }
    [Fact] public void OfficialMenuAdapterDoesNotBecomeRecipeOrAcceptAnotherHost()
    {
        const string html="<title>Cardápio — Remanso do Bosque</title><h2>PEIXES E FRUTOS DO MAR</h2><h4>Filhote assado na brasa</h4><h2>CERVEJAS</h2><h4>Cerveja</h4>";
        var dish=Assert.Single(RemansoMenuSource.Parse(html,RemansoMenuSource.Url,DateTime.UtcNow));
        Assert.Equal("dish_evidence",dish.Kind);Assert.Empty(dish.Ingredients);Assert.Equal("Belém",dish.City);
        Assert.Empty(RemansoMenuSource.Parse(html,"https://other.example/menu",DateTime.UtcNow));
    }
    [Theory]
    [InlineData("https://evil.example/embed/rJBEnyYwtbI")]
    [InlineData("https://www.youtube.com.evil.example/watch?v=rJBEnyYwtbI")]
    [InlineData("http://www.youtube.com/watch?v=rJBEnyYwtbI")]
    public void VideoDoesNotAcceptLookalikesOrInsecureUrls(string value)=>Assert.Null(CulinaryStructuredParser.Video(value));
    [Fact] public void LegacyEncodingPreservesPortuguese()
    {
        var bytes=Encoding.Latin1.GetBytes("<meta charset=iso-8859-1>Hambúrguer de camarão");
        Assert.Contains("Hambúrguer de camarão",CulinaryPageText.Decode(bytes,null));
        Assert.Equal("camarão",CulinaryPageText.Decode(Encoding.UTF8.GetBytes("camarão"),"utf-8"));
    }
    [Fact] public void TypoAndFollowUpPreserveDish()
    {
        Assert.Equal("hamburguer de camarão",CulinaryQuery.Correct("hamburguer de camaração"));
        Assert.Equal("Quero hambúrguer de camarão em Belém",CuiaConversation.Anchor("Mostre um vídeo",["Quero hambúrguer de camarão em Belém","Tem receita publicada?"]));
    }
    [Fact] public void OperationalSearchDoesNotShowGenericEncyclopediaOrUnrelatedCuisine()
    {
        var sources=new[]{new WebSearchSource("Culinária mineira","https://example.org/minas","receitas e ingredientes",DateTimeOffset.UtcNow){SourceKind="encyclopedia"},new WebSearchSource("Hambúrguer de camarão","https://example.org/receita","Receita com ingredientes",DateTimeOffset.UtcNow)};
        var selected=Assert.Single(SearchRanking.Merge("receita hambúrguer de camarão Belém",sources));
        Assert.EndsWith("/receita",selected.Url);
    }
    [Fact] public void EncyclopediaMustMatchSubjectInTitleNotJustGenericSnippet()
    {
        Assert.Empty(SearchRanking.Merge("tacacá",[new WebSearchSource("Pará","https://example.org/para","Há tacacá no Pará",DateTimeOffset.UtcNow){SourceKind="encyclopedia"}]));
    }
}
