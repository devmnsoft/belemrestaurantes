using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
namespace BelemRestaurantes.Infrastructure;

public sealed class Pbkdf2PasswordVerifier : IPasswordVerifier
{
    public string Hash(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return $"600000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrEmpty(password) || password.Length > 1024) return false;
        var parts = stored.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 100_000 || iterations > 2_000_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != 16 || expected.Length != 32)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public sealed class TotpProvider : ITotpProvider
{
    public string GenerateSecret()
    {
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    public string GenerateFor(DateTimeOffset instant, string base32Secret)
    {
        var counter = instant.ToUnixTimeSeconds() / 30;
        return Compute(counter, base32Secret);
    }

    public bool Validate(string code, string base32Secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit))
        {
            return false;
        }

        var counter = now.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            if (string.Equals(Compute(counter + offset, base32Secret), code, StringComparison.Ordinal))
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
        var binary = ((hash[offset] & 0x7F) << 24) |
                     ((hash[offset + 1] & 0xFF) << 16) |
                     ((hash[offset + 2] & 0xFF) << 8) |
                     (hash[offset + 3] & 0xFF);

        var otp = binary % 1_000_000;
        return otp.ToString("D6");
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
                throw new FormatException("Segredo TOTP inválido.");
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