using BelemRestaurantes.Application.Services;
namespace BelemRestaurantes.UnitTests;
public sealed class Sprint52ContentTests
{
    [Fact]public void CrawlerPrefersReachableFamilyWithoutDroppingIpv6Fallback()
    {
        var ipv4=System.Net.IPAddress.Parse("8.8.8.8");var ipv6=System.Net.IPAddress.Parse("2606:4700:4700::1111");
        Assert.Equal(new[]{ipv4,ipv6},BelemRestaurantes.Infrastructure.Crawler.SafeHttpClient.ConnectionOrder([ipv6,ipv4]));Assert.Equal(new[]{ipv6},BelemRestaurantes.Infrastructure.Crawler.SafeHttpClient.ConnectionOrder([ipv6]));
    }

    [Fact]public void CanonicalLinksRemoveTrackingAndRejectNonPublicDestinations()
    {
        Assert.Equal("https://example.org/materia?id=7",ExternalContentRules.CanonicalUrl("https://example.org/materia?utm_source=feed&id=7&fbclid=test#section"));
        foreach(var bad in new[]{"javascript:alert(1)","https://127.0.0.1/","https://example.local/","https://user:pass@example.org/","https://example.org:8443/"})Assert.Null(ExternalContentRules.CanonicalUrl(bad));
    }
    [Theory][InlineData("https://www.youtube.com/watch?v=abc",true)][InlineData("https://www.instagram.com/reel/abc",true)][InlineData("https://www.tiktok.com/@canal/video/123",true)][InlineData("https://www.instagram.com/perfil",false)][InlineData("https://evil.youtube.com/watch?v=abc",false)]
    public void VideoLinksAreDistinguishedFromProfiles(string url,bool expected)=>Assert.Equal(expected,ExternalContentRules.Video(url));
    [Fact]public void RelevanceRequiresPortalSubjectAndParaCity()
    {
        Assert.True(ExternalContentRules.Relevant("Restaurantes e sabores do Pará","Belém"));Assert.False(ExternalContentRules.Relevant("Eleições em Belém","Belém"));Assert.False(ExternalContentRules.Relevant("Saúde inicia oferta de novos medicamentos oncológicos no SUS","Belém"));Assert.False(ExternalContentRules.Relevant("Queda de desmatamento no Brasil gera recursos à economia global","Belém"));Assert.False(ExternalContentRules.Relevant("Comercialização de medicamentos","Belém"));Assert.False(ExternalContentRules.Relevant("Restaurantes de outra cidade","Lisboa"));
        Assert.True(ExternalContentRules.Regional("Culinária paraense em Belém","Belém"));Assert.False(ExternalContentRules.Regional("Restaurante para visitar em São Paulo","Belém"));Assert.Equal("https://www.youtube.com/watch?v=abc",ExternalContentRules.FindVideoLink("Vídeo: https://www.youtube.com/watch?v=abc"));
        var summary=ExternalContentRules.Summary("Belém","example.org",true);Assert.Contains("Referência em vídeo",summary);Assert.DoesNotContain("reportagem copiada",summary);
    }
}
