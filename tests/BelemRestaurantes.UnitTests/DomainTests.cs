using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.UnitTests.Domain;

public class NameNormalizerTests
{
    [Fact] public void Normalize_QuitsNoiseAndAccents() =>
        Assert.Equal("joao peixe belem pa", NameNormalizer.Normalize("Restaurante João Peixe - Belém/PA"));

    [Theory]
    [InlineData("Casa de Pasto Boi Azul", "boi azul")]
    [InlineData("Café do Açaí", "acai")] // accents é/á stripped
    public void Normalize_CanonicalizesNames(string raw, string expectedSubstring)
    {
        var result = NameNormalizer.Normalize(raw);
        Assert.Contains(expectedSubstring, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Similar_Restaurants_Produce_Same_Key()
    {
        var a = NameNormalizer.Normalize("Casa de Pasto Boi Azul");
        var b = NameNormalizer.Normalize("CASA DE PASTO BOI AZUL");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_Name_Returns_Empty() =>
        Assert.Equal(string.Empty, NameNormalizer.Normalize("   "));

    [Fact]
    public void ToDisplayCase_CapitalizesProperly() =>
        Assert.Equal("Casa de Pasto Boi Azul", NameNormalizer.ToDisplayCase("casa de pasto BOI AZUL"));
}

public class DeduplicationTests
{
    [Fact]
    public void SameExternalId_Merges()
    {
        var catalog = new[] { new DeduplicationCandidate(1, "boi azul", "belem", "places/abc") };
        var result = DeduplicationService.Compare("places/abc", "Boi Azul", "Belém", catalog);
        Assert.Equal(MatchOutcome.SameExternalId, result.Outcome);
        Assert.Equal(1L, result.MatchedRestaurantId);
        Assert.True(result.IsSafeToAutoMerge);
    }

    [Fact]
    public void SameNameSameCity_IsProbableDuplicate()
    {
        var catalog = new[] { new DeduplicationCandidate(1, "boi azul", "belem") };
        var result = DeduplicationService.Compare(null, "Boi Azul", "Belém", catalog);
        Assert.Equal(MatchOutcome.ProbableDuplicate, result.Outcome);
        Assert.True(result.RequiresHumanReview);
        // Never auto-publishes.
        Assert.False(result.IsSafeToAutoMerge);
    }

    [Fact]
    public void NewRestaurant_IsNew()
    {
        var catalog = Array.Empty<DeduplicationCandidate>();
        var result = DeduplicationService.Compare("x", "Novissimo Lugar", "Belém", catalog);
        Assert.Equal(MatchOutcome.New, result.Outcome);
    }

    [Fact]
    public void CrossCity_Ambiguity()
    {
        var catalog = new[] { new DeduplicationCandidate(1, "boi azul", "maraba") };
        var result = DeduplicationService.Compare(null, "Boi Azul", "Belém", catalog);
        Assert.Equal(MatchOutcome.PossibleAmbiguity, result.Outcome);
    }
}

public class SocialLinkTests
{
    [Fact]
    public void HttpsRequired()
    {
        Assert.False(SocialLinkRules.TryValidate("http://evil.com/x", out _));
        Assert.False(SocialLinkRules.TryValidate("javascript:alert(1)", out _));
    }

    [Fact]
    public void ValidHttpsAccepted()
    {
        Assert.True(SocialLinkRules.TryValidate("https://example.com/x?y=1", out var safe));
        Assert.Equal("https://example.com/x?y=1", safe);
    }

    [Fact]
    public void MatchesExpectedHost()
    {
        Assert.True(SocialLinkRules.MatchesHost("https://www.instagram.com/x", "instagram.com"));
        Assert.False(SocialLinkRules.MatchesHost("https://evil.example.com/", "instagram.com"));
    }
}

public class AdminPasswordPolicyTests
{
    [Fact]
    public void WeakPasswordRejected()
    {
        var failures = AdminPasswordPolicy.Validate("Senha123");
        Assert.NotEmpty(failures);
    }

    [Fact]
    public void StrongPasswordAccepted()
    {
        var failures = AdminPasswordPolicy.Validate("Abcdefghij1!Zyz");
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(1, false)]
    public void LockoutDecision(int failuresBefore, bool expectsLockout)
    {
        var user = new AdminUser { Id = "u", AccessFailedCount = failuresBefore };
        var decision = AdminPasswordPolicy.RegisterFailure(user, DateTimeOffset.UtcNow);
        Assert.Equal(expectsLockout, decision.ShouldLockOut);
    }
}
