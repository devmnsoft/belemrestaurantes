using System.Text.RegularExpressions;

namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Regras de moderação de conteúdo, filtros de linguagem ofensiva e sanitização contra PII (dados sensíveis).
/// </summary>
public static class ContentModerationRules
{
    private static readonly string[] BadWords =
    [
        "merda", "bosta", "caralho", "puta", "puto", "porra", "foder", "foda",
        "arrombado", "filho da puta", "vagabundo", "desgraçado", "lixo de lugar",
        "otario", "escroto", "safado", "canalha"
    ];

    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\b(?:\+?55\s?)?(?:\(?\d{2}\)?\s?)?(?:9\d{4}|\d{4})[-\s]?\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex CreditCardRegex = new(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public sealed record ModerationAnalysis(bool ContainsOffensiveLanguage, bool ContainsPii, string? CleanedText, string? ViolationReason);

    public static ModerationAnalysis Analyze(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ModerationAnalysis(false, false, null, null);
        }

        var text = rawText.Trim();
        // Remove tags HTML para evitar injeção
        text = HtmlTagRegex.Replace(text, string.Empty).Trim();

        var lowered = text.ToLowerInvariant();

        // Checagem de palavras ofensivas
        bool containsBadWord = false;
        string? badWordFound = null;
        foreach (var badWord in BadWords)
        {
            if (lowered.Contains(badWord, StringComparison.OrdinalIgnoreCase))
            {
                containsBadWord = true;
                badWordFound = badWord;
                break;
            }
        }

        // Checagem de PII (CPF, telefone, cartão)
        bool containsPii = false;
        string? piiReason = null;
        if (CpfRegex.IsMatch(text))
        {
            containsPii = true;
            piiReason = "Divulgação de dados pessoais (CPF detectado).";
        }
        else if (CreditCardRegex.IsMatch(text))
        {
            containsPii = true;
            piiReason = "Divulgação de dados pessoais sensíveis (cartão detectado).";
        }

        string? reason = (containsBadWord, containsPii) switch
        {
            (true, true) => $"Linguagem ofensiva ('{badWordFound}') e {piiReason}",
            (true, false) => $"Linguagem ofensiva detectada: '{badWordFound}'",
            (false, true) => piiReason,
            _ => null
        };

        return new ModerationAnalysis(containsBadWord, containsPii, text, reason);
    }
}
