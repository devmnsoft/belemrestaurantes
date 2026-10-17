using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class ContentModerationTests
{
    [Fact]
    public void CleanText_PassesCleanReview()
    {
        var input = "O peixe filhote com molho de tucupi estava excelente! Recomendo o açaí tradicional.";
        var result = ContentModerationRules.Analyze(input);

        Assert.False(result.ContainsOffensiveLanguage);
        Assert.False(result.ContainsPii);
        Assert.Equal(input, result.CleanedText);
        Assert.Null(result.ViolationReason);
    }

    [Theory]
    [InlineData("Atendimento uma bosta completa")]
    [InlineData("Lugar de merda, comida fria")]
    [InlineData("Garçom vagabundo")]
    public void OffensiveLanguage_IsDetectedAndFlagged(string offensiveText)
    {
        var result = ContentModerationRules.Analyze(offensiveText);
        Assert.True(result.ContainsOffensiveLanguage);
        Assert.NotNull(result.ViolationReason);
    }

    [Theory]
    [InlineData("Meu CPF é 123.456.789-00 para a nota")]
    [InlineData("Contato do gerente 98765432100")]
    public void PiiDetection_DetectsCpfOrSensitiveData(string textWithPii)
    {
        var result = ContentModerationRules.Analyze(textWithPii);
        Assert.True(result.ContainsPii);
        Assert.Contains("CPF", result.ViolationReason);
    }

    [Fact]
    public void HtmlTags_AreStrippedToPreventXss()
    {
        var raw = "Ótimo lugar! <script>alert('xss')</script> Adorei.";
        var result = ContentModerationRules.Analyze(raw);

        Assert.False(result.CleanedText?.Contains("<script>"));
        Assert.Contains("Ótimo lugar!", result.CleanedText);
    }
}
