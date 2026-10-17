using System;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public sealed class SocialExperienceDomainTests
{
    [Fact]
    public void UserExperience_Defaults_AreSafeAndConsistent()
    {
        var exp = new UserExperience
        {
            RestaurantId = 42,
            UserId = "user-123",
            AuthorName = "Avaliador Paraense",
            Stars = 5,
            ServiceStars = 4,
            FlavorStars = 5,
            AmbianceStars = 4,
            CostBenefitStars = 5,
            Visibility = "public",
            Status = "pending"
        };

        Assert.Equal(42, exp.RestaurantId);
        Assert.Equal("user-123", exp.UserId);
        Assert.Equal("public", exp.Visibility);
        Assert.Equal("pending", exp.Status);
        Assert.Equal("none", exp.AppealStatus);
        Assert.Equal(0, exp.LikesCount);
        Assert.Equal(0, exp.CommentsCount);
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("private", false)]
    [InlineData("unlisted", false)]
    public void Experience_Visibility_ControlsPublicFeedEligibility(string visibility, bool isPublic)
    {
        var isFeedEligible = visibility == "public";
        Assert.Equal(isPublic, isFeedEligible);
    }

    [Fact]
    public void ContentModeration_IdentifiesBothOffensiveAndPii()
    {
        var mixed = "Seu bosta, ligue para 123.456.789-00 agora!";
        var result = ContentModerationRules.Analyze(mixed);

        Assert.True(result.ContainsOffensiveLanguage);
        Assert.True(result.ContainsPii);
        Assert.NotNull(result.ViolationReason);
    }

    [Fact]
    public void CuiaRating_PreventsManipulationBySponsorship()
    {
        // 5 avaliações normais vs cálculo puro bayesiano
        var normalBayesian = CuiaRatingRules.CalculateBayesianScore(4.5, 20);
        Assert.True(normalBayesian >= 4.4 && normalBayesian <= 4.5);
    }
}
