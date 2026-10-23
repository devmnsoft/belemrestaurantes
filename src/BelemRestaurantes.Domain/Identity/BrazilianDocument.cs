namespace BelemRestaurantes.Domain.Identity;

public static class BrazilianDocument
{
    public static string Normalize(string? value) => string.Concat((value ?? "").Where(c => c is not ('.' or '-' or '/' or ' '))).ToUpperInvariant();

    public static bool IsCpf(string? value)
    {
        var text = Normalize(value);
        if (text.Length != 11 || !text.All(char.IsAsciiDigit) || text.Distinct().Count() == 1) return false;
        for (var length = 9; length <= 10; length++)
        {
            var sum = 0;
            for (var i = 0; i < length; i++) sum += (text[i] - '0') * (length + 1 - i);
            var digit = (sum * 10) % 11;
            if (digit == 10) digit = 0;
            if (digit != text[length] - '0') return false;
        }
        return true;
    }

    // Receita Federal: ASCII - 48, including letters; final two positions stay numeric.
    public static bool IsCnpj(string? value)
    {
        var text = Normalize(value);
        if (text.Length != 14 || text.Distinct().Count() == 1 || !text[..12].All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'Z') || !text[12..].All(char.IsAsciiDigit)) return false;
        for (var length = 12; length <= 13; length++)
        {
            var sum = 0; var weight = 2;
            for (var i = length - 1; i >= 0; i--) { sum += (text[i] - 48) * weight; weight = weight == 9 ? 2 : weight + 1; }
            var rest = sum % 11;
            if ((rest < 2 ? 0 : 11 - rest) != text[length] - '0') return false;
        }
        return true;
    }
}
