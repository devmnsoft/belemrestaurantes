using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Photos;

namespace BelemRestaurantes.UnitTests;

public sealed class LicensedPhotoTests
{
    [Fact]
    public void OfficialSiteImage_RequiresSameHostAndBlocksPlatforms()
    {
        Assert.True(LicensedImageRules.TryOfficialSiteImage("https://casa.example/cardapio", "https://www.casa.example/foto.jpg", out var safe));
        Assert.EndsWith("/foto.jpg", safe);
        Assert.False(LicensedImageRules.TryOfficialSiteImage("https://casa.example/", "https://lh3.googleusercontent.com/foto.jpg", out _));
        Assert.False(LicensedImageRules.TryOfficialSiteImage("https://casa.example/", "https://upload.wikimedia.org/wikipedia/commons/a/ab/Foto.jpg", out _));
        Assert.False(LicensedImageRules.TryOfficialSiteImage("https://casa.example/", "https://outra.example/foto.jpg", out _));
    }

    [Theory]
    [InlineData("CC BY-SA 4.0", "cc-by-sa")]
    [InlineData("CC BY 4.0", "cc-by")]
    [InlineData("CC0 1.0", "cc0")]
    [InlineData("Public domain", "pd")]
    public void FreeLicenses_AreAccepted(string name, string code)
    {
        Assert.True(LicensedImageRules.TryFreeLicense(name, out var parsed));
        Assert.Equal(code, parsed);
    }

    [Theory]
    [InlineData("CC BY-NC 4.0")]
    [InlineData("CC BY-ND 4.0")]
    [InlineData("All rights reserved")]
    public void NonFreeLicenses_AreRejected(string name) => Assert.False(LicensedImageRules.TryFreeLicense(name, out _));

    [Fact]
    public void StrongMatch_NeedsPlaceNameAndCity_AndRejectsLogos()
    {
        Assert.True(LicensedImageRules.StrongPlaceMatch("Amazon Beer", "Belém", "File:Amazon Beer Belém facade.jpg", "Estação das Docas"));
        Assert.False(LicensedImageRules.StrongPlaceMatch("Bar", "Belém", "File:Bar Belém.jpg", null));
        Assert.False(LicensedImageRules.StrongPlaceMatch("Amazon Beer", "Belém", "File:Amazon Beer logo.png", "Belém"));
        Assert.False(LicensedImageRules.StrongPlaceMatch("Amazon Beer", "Belém", "File:Ver-o-Peso Belém.jpg", null));
        Assert.False(LicensedImageRules.StrongPlaceMatch("REFEITÓRIO", "Belém", "File:Lisboa, Mosteiro dos Jerónimos, refeitório, azulejos (2).jpg", "Refeitório em Belém, Lisboa"));
    }

    [Fact]
    public void WikimediaParser_KeepsFreeStrongMatch_AndDropsTheRest()
    {
        const string json = """
        {"query":{"pages":{
          "1":{"title":"File:Amazon Beer Belém.jpg","imageinfo":[{"url":"https://upload.wikimedia.org/wikipedia/commons/a/ab/Amazon_Beer.jpg","thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/a/ab/Amazon_Beer.jpg/960px-Amazon_Beer.jpg","mime":"image/jpeg","size":240000,"extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"<a href=\"https://example.test\">Ana</a>"},"ImageDescription":{"value":"Amazon Beer em Belém"}}}]},
          "2":{"title":"File:Amazon Beer Belém private.jpg","imageinfo":[{"url":"https://upload.wikimedia.org/wikipedia/commons/b/bb/Private.jpg","mime":"image/jpeg","size":240000,"extmetadata":{"LicenseShortName":{"value":"CC BY-NC 4.0"},"Artist":{"value":"Ana"}}}]},
          "3":{"title":"File:Outro lugar.jpg","imageinfo":[{"url":"https://upload.wikimedia.org/wikipedia/commons/c/cc/Outro.jpg","mime":"image/jpeg","size":240000,"extmetadata":{"LicenseShortName":{"value":"CC0"},"Artist":{"value":"Ana"},"ImageDescription":{"value":"Belém"}}}]},
          "4":{"title":"File:Amazon Beer Belém.gif","imageinfo":[{"url":"https://cdn.example/foto.jpg","mime":"image/gif","size":240000,"extmetadata":{"LicenseShortName":{"value":"CC0"}}}]}
        }}}
        """;
        var photos = WikimediaPhotoParser.Read(json, "Amazon Beer", "Belém");
        var photo = Assert.Single(photos, item => item.StrongMatch);
        Assert.Equal(2, photos.Count);
        Assert.Equal("cc-by-sa", photo.LicenseType);
        Assert.Contains("Ana", photo.Attribution);
        Assert.StartsWith("https://upload.wikimedia.org/", photo.MediaUrl);
        Assert.StartsWith("https://commons.wikimedia.org/wiki/", photo.SourcePage);
    }

    [Fact]
    public void TasteCatalog_HasUniqueSlugs()
    {
        Assert.Equal(ParaTasteCatalog.All.Count, ParaTasteCatalog.All.Select(entry => entry.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotNull(ParaTasteCatalog.Find("tacaca"));
        Assert.Null(ParaTasteCatalog.Find("nao-existe"));
    }
}
