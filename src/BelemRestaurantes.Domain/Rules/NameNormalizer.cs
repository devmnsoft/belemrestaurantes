using System.Globalization;
using System.Text;

namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Normaliza nomes para permitir deduplicação. O mesmo restaurante aparece no
/// Places como "Casa de Pasto Boi Azul", "Casa Do Pasto Boi Azul" ou
/// "Casa de Pasto Boi Azul - Belém"; os três precisam cair na mesma chave.
/// </summary>
public static class NameNormalizer
{
    /// <summary>Palavras que não carregam identidade e são removidas da chave.</summary>
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "ltda", "me", "eireli", "sa", "s", "a", "epp", "restaurante", "restauracao", "lanches",
        "pizzaria", "cafe", "cafeteria", "churrascaria", "bar", "house", "food", "comercio",
        "lanches", "delivery", "express"
    };

    /// <summary>
    /// Produz a chave de comparação: minúsculo, sem acento, sem pontuação e sem
    /// palavras genéricas ou cities. Retorna string vazia se nada restar.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var decomposed = RemoveDiacritics(raw).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = true;

        foreach (var ch in decomposed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                // qualquer separador (espaço, hífen, barra, ponto) vira um único espaço
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        var tokens = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !NoiseWords.Contains(t))
            .ToArray();

        return string.Join(' ', tokens);
    }

    /// <summary>Remove acentos combinando diacríticos (NFD + descarte das marcas).</summary>
    public static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Capitaliza respeitando preposições, como se escreve o nome no cartão.</summary>
    public static string ToDisplayCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var words = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder(value.Length);
        var capitalizeNext = true;

        foreach (var word in words)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // "de", "do", "da", "e" ficam minúsculas salvo início de frase.
            var keepLower = !capitalizeNext && Prepositions.Contains(word.ToLowerInvariant());
            var proper = char.ToUpper(word[0], CultureInfo.GetCultureInfo("pt-BR")) + word[1..].ToLowerInvariant();
            builder.Append(keepLower ? word.ToLowerInvariant() : proper);

            capitalizeNext = false;
        }

        return builder.ToString();
    }

    private static readonly HashSet<string> Prepositions = new(StringComparer.Ordinal)
    {
        "de", "do", "da", "dos", "das", "e", "em", "a", "o"
    };
}