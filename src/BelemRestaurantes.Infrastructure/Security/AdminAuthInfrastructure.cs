using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace BelemRestaurantes.Infrastructure.Security;

/// <summary>Hash de senha via PBKDF2 (RFC 2898), sem dependência de pacote.</summary>
public sealed class Pbkdf2PasswordVerifier : IPasswordVerifier
{
    private readonly int Iterations = 150_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var iterations) ||
            !TryBase64(parts[1], out var salt) ||
            !TryBase64(parts[2], out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryBase64(string value, out byte[] output)
    {
        try
        {
            output = Convert.FromBase64String(value);
            return output.Length > 0;
        }
        catch (FormatException)
        {
            output = [];
            return false;
        }
    }
}

/// <summary>TOTP (RFC 6238) sobre SHA1, com janela de ±1 período de 30 s.</summary>
public sealed class TotpProvider : ITotpProvider
{
    private const int StepSeconds = 30;
    private const int CodeDigits = 6;

    public string GenerateSecret()
    {
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    public string GenerateFor(DateTimeOffset instant, string base32Secret)
    {
        var counter = instant.ToUnixTimeSeconds() / StepSeconds;
        return Compute(counter, base32Secret);
    }

    public bool Validate(string code, string base32Secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != CodeDigits || !code.All(char.IsDigit))
        {
            return false;
        }

        for (var offset = -1; offset <= 1; offset++)
        {
            var counter = (now.ToUnixTimeSeconds() / StepSeconds) + offset;
            if (string.Equals(Compute(counter, base32Secret), code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Compute(long counter, string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(message);

        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, CodeDigits);
        return otp.ToString($"D{CodeDigits}");
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder(data.Length * 8 / 5 + 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            buffer <<= 5 - bitsLeft;
            result.Append(alphabet[buffer & 0x1F]);
        }

        return result.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = value.ToUpperInvariant().Replace("=", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in cleaned)
        {
            var index = alphabet.IndexOf(ch);
            if (index < 0)
            {
                throw new FormatException("Segredo TOTP em base32 inválido.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)(buffer >> bitsLeft));
            }
        }

        return output.ToArray();
    }
}