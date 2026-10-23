using System.Text;
using System.Text.RegularExpressions;

namespace BelemRestaurantes.Infrastructure.Crawler;

public static class CulinaryPageText
{
    public static string Decode(byte[] bytes, string? declaredCharset)
    {
        var charset = declaredCharset?.Trim('"', '\'').ToLowerInvariant();
        if (string.IsNullOrEmpty(charset))
        {
            var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var match = Regex.Match(header, """charset\s*=\s*["']?([a-zA-Z0-9_-]+)""", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            if (match.Success) charset = match.Groups[1].Value.ToLowerInvariant();
        }
        if (charset is "iso-8859-1" or "latin1" or "windows-1252")
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(charset == "windows-1252" ? 1252 : 28591).GetString(bytes);
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
