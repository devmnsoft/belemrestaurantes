using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class CuiaRatingTests
{
    [Fact]
    public void CalculateBayesianScore_ZeroReviews_ReturnsZero()
    {
        var score = CuiaRatingRules.CalculateBayesianScore(5.0, 0);
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void CalculateBayesianScore_CalibratesAgainstSingleReviewOutlier()
    {
        // Um restaurante com apenas 1 avaliação 5 estrelas não deve obter score 5.0
        // (1 * 5 + 3 * 4) / 4 = 17 / 4 = 4.25
        var singleReviewScore = CuiaRatingRules.CalculateBayesianScore(5.0, 1);
        Assert.Equal(4.25, singleReviewScore);

        // Um restaurante com 50 avaliações de média 4.8 terá score muito mais alto e confiável
        // (50 * 4.8 + 3 * 4) / 53 = 252 / 53 = 4.75
        var solidScore = CuiaRatingRules.CalculateBayesianScore(4.8, 50);
        Assert.Equal(4.75, solidScore);

        Assert.True(solidScore > singleReviewScore, "Restaurante com volume e consistência deve superar outlier com 1 review isolado.");
    }

    [Fact]
    public void MethodologySummary_ReturnsTransparentFormulaExplanation()
    {
        var summary = CuiaRatingRules.GetMethodologySummary(4.5, 10, 4.38);
        Assert.Contains("média bayesiana", summary);
        Assert.Contains("4.38/5.00", summary);
        Assert.Contains("10 avaliação(ões)", summary);
        Assert.Contains("fator de calibração", summary);
    }

    [Theory]
    [InlineData("melhores_belem", "Belém", "Restaurante", 5.0, 10, true)]
    [InlineData("melhores_belem", "Santarém", "Restaurante", 5.0, 10, false)]
    [InlineData("melhor_acai", "Belém", "Açaí do Porto", 5.0, 5, true)]
    [InlineData("melhor_tacaca", "Belém", "Tacacá da Dona Maria", 5.0, 5, true)]
    [InlineData("melhor_pizzaria", "Belém", "Pizzaria Forno a Lenha", 4.5, 3, true)]
    [InlineData("melhor_custo_beneficio", "Belém", "Buffet Popular", 4.8, 2, true)]
    [InlineData("destaque_interior", "Salinópolis", "Peixaria do Sal", 4.9, 8, true)]
    [InlineData("destaque_interior", "Belém", "Restaurante Central", 5.0, 20, false)]
    public void MatchesCuiaCategory_ClassifiesCorrectly(string category, string city, string cat, double costStars, int favs, bool expected)
    {
        var matches = CuiaRatingRules.MatchesCuiaCategory(category, city, cat, costStars, favs, DateTimeOffset.UtcNow);
        Assert.Equal(expected, matches);
    }
}
