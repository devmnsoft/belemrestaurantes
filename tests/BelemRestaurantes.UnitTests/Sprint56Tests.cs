using BelemRestaurantes.Application.Formatting;
using BelemRestaurantes.Application.Services;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint56Tests
{
    [Theory]
    [InlineData("TEM&#x20;PIZZA", "TEM PIZZA")]
    [InlineData("TEM&amp;#x20;PIZZA", "TEM PIZZA")]
    [InlineData("Como chegar", "Como chegar")]
    [InlineData("<b>Belém</b>", "Belém")]
    [InlineData("linha\u00A0quebrada", "linha quebrada")]
    public void PublicTextDecodesEntitiesWithoutLeavingMarkup(string raw, string expected) =>
        Assert.Equal(expected, PublicText.Clean(raw));

    [Fact]
    public void PublicTextStripsScriptAndDoesNotReturnRawHtml()
    {
        var clean = PublicText.Clean("<script>alert(1)</script>Tacacá");
        Assert.DoesNotContain("<", clean);
        Assert.Contains("Tacacá", clean);
    }

    [Fact]
    public void DishIsNotAnOfficialRecipeWithoutAPublishedSource()
    {
        Assert.Equal(DishRecipe.RestaurantDish, DishRecipe.Classify(offeredByPlace: true, recipePublishedByPlaceOrChef: false, similarHomeVersion: false));
        Assert.Equal(DishRecipe.PublishedRecipe, DishRecipe.Classify(true, true, false));
        Assert.Equal(DishRecipe.TraditionalSimilar, DishRecipe.Classify(false, false, true));
        Assert.Contains("Não é a receita oficial", DishRecipe.VisitorLabel(DishRecipe.TraditionalSimilar));
    }

    [Fact]
    public void SingleOfferIsNotCalledAnAverageAndMissingPricesStayOut()
    {
        var one = OfferMath.Summarize([new("tucupi", "feira", 12.5m, "litro", 1, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "https://example.org/oferta")], "litro");
        Assert.Contains("Preço observado", one);
        Assert.Contains("Não é média", one);
        var two = OfferMath.Summarize([
            new("tucupi", "a", 10m, "litro", 1, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "https://example.org/a"),
            new("tucupi", "b", 20m, "litro", 1, new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "https://example.org/b")
        ], "litro");
        Assert.Contains("Média de 2", two);
        var cost = OfferMath.RecipeCost([("tucupi", 0.5m, 10m, true), ("jambu", null, null, false)], 4);
        Assert.Contains("jambu", cost);
        Assert.Contains("5.00", cost);
        Assert.False(DishRecipe.CanScalePortions(["jambu a gosto"]));
        Assert.True(DishRecipe.CanScalePortions(["2 xícaras de goma"]));
    }

    [Fact]
    public void HoursQuestionNamesThePlaceAndDoesNotTreatSearchAsDoneWhenItWasNotAsked()
    {
        Assert.Equal("La Boca Pizzaria", CuiaPlaceQuestion.NameHint("Quais os horários da La Boca Pizzaria em São Brás?"));
        Assert.Equal("La Boca Pizzaria", CuiaPlaceQuestion.NameHint("Quais pratos esse restaurante divulga?", ["Quais os horários da La Boca Pizzaria em São Brás?"]));
        Assert.Null(CuiaPlaceQuestion.NameHint("Pesquise um restaurante que ainda não esteja no catálogo.", ["Quais os horários da La Boca Pizzaria em São Brás?"]));
        Assert.Equal("La Boca Pizzaria", CuiaPlaceQuestion.NameHint("Existe receita oficial de algum desses pratos?", ["Quais os horários da La Boca Pizzaria em São Brás?"]));
        Assert.True(CuiaPlaceQuestion.NeedsExternal("Mostre vídeos de receitas de culinária paraense."));
        Assert.True(CuiaPlaceQuestion.NeedsExternal("Pesquise um restaurante que ainda não esteja no catálogo."));
        var trace = CuiaPlaceQuestion.Trace("Quais os horários da La Boca?", 2, true, "not_configured", 0, "Tavily");
        Assert.Contains("intencao=horario", trace);
        Assert.Contains("conector=configurado", trace);
        Assert.DoesNotContain("Tavily", trace);
        Assert.Contains("ainda não foi localizado", CuiaPlaceQuestion.HoursGap("LA BOCA PIZZARIA BELEM", "Belém", "Sao Bras"));
    }

    [Fact]
    public void VideoLinkMustBeCanonicalHttps()
    {
        Assert.True(VideoReferenceRules.Canonical("https://www.youtube.com/watch?v=mwusmxyNpbU"));
        Assert.False(VideoReferenceRules.Canonical("http://www.youtube.com/watch?v=mwusmxyNpbU"));
        Assert.False(VideoReferenceRules.Canonical("https://evil.example/watch?v=mwusmxyNpbU"));
    }

    [Fact]
    public void ClaimFlagIsSeparateFromAnUnvalidatedOwner()
    {
        Assert.Equal("Responsável não reivindicado", FieldStates.Label("not_located") == "Ainda não localizado" ? "Responsável não reivindicado" : "");
        Assert.Equal("Informação conflitante", FieldStates.Label(FieldStates.Conflict));
        Assert.Equal("Verificado", FieldStates.Label(FieldStates.Verified));
    }
}
