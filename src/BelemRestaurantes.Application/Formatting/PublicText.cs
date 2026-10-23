using System.Net;
using System.Text.RegularExpressions;

namespace BelemRestaurantes.Application.Formatting;

/// <summary>
/// Texto público vindo de fontes externas. Decodifica entidades e remove marcação.
/// O resultado continua sendo texto: a view HTML o codifica de novo.
/// </summary>
public static class PublicText
{
    public static string Clean(string? value, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Replace('\u00A0', ' ').Replace('\u200B', ' ').Replace('\uFEFF', ' ');
        for (var pass = 0; pass < 3 && text.Contains('&', StringComparison.Ordinal); pass++)
        {
            var decoded = WebUtility.HtmlDecode(text);
            if (decoded == text) break;
            text = decoded;
        }
        text = Regex.Replace(text, "<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
        if (text.Length > max) text = text[..max].TrimEnd();
        return text;
    }

    public static string? Show(string? value, int max = 500)
    {
        var clean = Clean(value, max);
        return clean.Length == 0 ? null : clean;
    }
}
