using System.Security.Cryptography;
using System.Text;

namespace BelemRestaurantes.Domain.Identity;

/// <summary>
/// Códigos de uso único para recuperar acesso quando o app TOTP não está
/// disponível. Cada código é mostrado uma única vez e guardado apenas como
/// hash SHA-256 — o valor em claro nunca persiste.
/// </summary>
public static class RecoveryCodes
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static IReadOnlyList<string> Generate(int count, RandomNumberGenerator? rng = null)
    {
        rng ??= RandomNumberGenerator.Create();
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add($"{RandomGroup(rng)}-{RandomGroup(rng)}");
        }

        return codes;
    }

    /// <summary>Normaliza antes de comparar: o usuário digita com ou sem traço/espaço.</summary>
    public static string Normalize(string code) =>
        new string(code.Trim().ToUpperInvariant().Where(static c => c is not (' ' or '-')).ToArray());

    /// <summary>Aceita apenas o formato emitido, com ou sem o traço.</summary>
    public static bool TryNormalize(string? code, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32)
        {
            return false;
        }

        normalized = Normalize(code);
        if (normalized.Length != 10 || normalized.Any(c => Alphabet.IndexOf(c) < 0))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    public static string Hash(string normalizedCode)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(hash);
    }

    private static string RandomGroup(RandomNumberGenerator rng)
    {
        var bytes = new byte[5];
        rng.GetBytes(bytes);
        var chars = new char[5];
        for (var i = 0; i < 5; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
