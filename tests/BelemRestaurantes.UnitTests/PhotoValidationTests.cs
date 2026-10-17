using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class PhotoValidationTests
{
    [Fact]
    public void Jpeg_MagicBytes_AreRecognized()
    {
        byte[] jpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
        var isSupported = PhotoRules.IsSupportedMagicBytes(jpegHeader, out var format);

        Assert.True(isSupported);
        Assert.Equal("image/jpeg", format);
    }

    [Fact]
    public void Png_MagicBytes_AreRecognized()
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        var isSupported = PhotoRules.IsSupportedMagicBytes(pngHeader, out var format);

        Assert.True(isSupported);
        Assert.Equal("image/png", format);
    }

    [Fact]
    public void Webp_MagicBytes_AreRecognized()
    {
        byte[] webpHeader = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];
        var isSupported = PhotoRules.IsSupportedMagicBytes(webpHeader, out var format);

        Assert.True(isSupported);
        Assert.Equal("image/webp", format);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.sh")]
    [InlineData("page.html")]
    [InlineData("exploit.php")]
    public void DangerousExtensions_AreRejected(string dangerousFileName)
    {
        Assert.False(PhotoRules.IsValidExtension(dangerousFileName));
    }

    [Theory]
    [InlineData("foto_prato.jpg")]
    [InlineData("sobremesa.jpeg")]
    [InlineData("ambiente.png")]
    [InlineData("peixe.webp")]
    public void SafeExtensions_AreAccepted(string safeFileName)
    {
        Assert.True(PhotoRules.IsValidExtension(safeFileName));
    }
}
