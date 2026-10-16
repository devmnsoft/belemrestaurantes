namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Regras de validação técnica e sanitização de uploads de fotografias.
/// </summary>
public static class PhotoRules
{
    public const long MaxFileSizeBytes = 8 * 1024 * 1024; // 8 MB
    public const int MaxPhotosPerExperience = 5;
    public const int MaxDimensionPixels = 1920;
    public const int ThumbnailDimensionPixels = 400;

    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffHeader = [0x52, 0x49, 0x46, 0x46]; // RIFF....WEBP

    public static bool IsSupportedMagicBytes(ReadOnlySpan<byte> header, out string format)
    {
        format = string.Empty;
        if (header.Length < 12) return false;

        if (header.StartsWith(JpegHeader))
        {
            format = "image/jpeg";
            return true;
        }

        if (header.StartsWith(PngHeader))
        {
            format = "image/png";
            return true;
        }

        if (header.StartsWith(RiffHeader) &&
            header[8] == (byte)'W' && header[9] == (byte)'E' &&
            header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            format = "image/webp";
            return true;
        }

        return false;
    }

    public static bool IsValidExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }
}
