using System.Globalization;
using System.Xml.Linq;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
namespace BelemRestaurantes.UnitTests;
// Sprint 76: o portal ganhou /robots.txt e /sitemap.xml. O builder é função pura — aqui se prova o
// contrato do XML (namespace oficial, loc absoluto, lastmod no subconjunto W3C, escapamento, teto
// por arquivo, origem HTTPS) sem banco nem HTTP.
public sealed class Sprint76SitemapTests
{
    [Fact]public void BuildProducesValidUrlsetWithAbsoluteLocsAndOptionalLastmod()
    {
        var entries = new List<SitemapEntry>
        {
            new("/guia", null),
            new("/estabelecimento/42", new DateTimeOffset(2026, 10, 24, 6, 30, 0, TimeSpan.Zero)),
            new("/receitas/tacaca-em-casa", new DateTimeOffset(2026, 10, 20, 23, 15, 0, TimeSpan.FromHours(-3)))
        };
        var xml = SitemapBuilder.Build("https://belemrestaurantes.com.br", entries);
        var doc = XDocument.Parse(xml); // quebra aqui se o XML for inválido
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        Assert.Equal("urlset", doc.Root!.Name.LocalName);
        var urls = doc.Root.Elements(ns + "url").ToList();
        Assert.Equal(3, urls.Count);
        Assert.Equal("https://belemrestaurantes.com.br/guia", urls[0].Element(ns + "loc")!.Value);
        Assert.Null(urls[0].Element(ns + "lastmod")); // lastmod ausente é legítimo para rota estável
        Assert.Equal("https://belemrestaurantes.com.br/estabelecimento/42", urls[1].Element(ns + "loc")!.Value);
        Assert.Equal("2026-10-24T06:30:00Z", urls[1].Element(ns + "lastmod")!.Value);
        Assert.Equal("2026-10-21T02:15:00Z", urls[2].Element(ns + "lastmod")!.Value); // fuso -03:00 normaliza para UTC/Z
    }
    [Fact]public void BuildEscapesXmlSpecialCharactersInPaths()
    {
        var xml = SitemapBuilder.Build("https://x.br", [new SitemapEntry("/categoria/burger-&-beer", null)]);
        var doc = XDocument.Parse(xml);
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        Assert.Equal("https://x.br/categoria/burger-&-beer", doc.Root!.Elements(ns + "url").First().Element(ns + "loc")!.Value);
        Assert.Contains("&amp;", xml);
        Assert.Equal("&lt;b&gt;&amp;&quot;", SitemapBuilder.XmlEscape("<b>&\""));
    }
    [Fact]public void BuildRefusesToSilentlyTruncateAboveTheProtocolCap()
    {
        var entries = new List<SitemapEntry>(SitemapBuilder.MaxUrlsPerFile + 1);
        for (var i = 0; i <= SitemapBuilder.MaxUrlsPerFile; i++) entries.Add(new SitemapEntry("/estabelecimento/" + i, null));
        var ex = Assert.Throws<InvalidOperationException>(() => SitemapBuilder.Build("https://x.br", entries));
        Assert.Contains("50000", ex.Message);
    }
    [Fact]public void ResolvePublicOriginUsesOfficialDefaultAndRequiresHttps()
    {
        Assert.Equal("https://belemrestaurantes.com.br", SitemapBuilder.ResolvePublicOrigin(null));
        Assert.Equal("https://belemrestaurantes.com.br", SitemapBuilder.ResolvePublicOrigin("  "));
        Assert.Equal("https://homolog.belemrestaurantes.com.br", SitemapBuilder.ResolvePublicOrigin("https://homolog.belemrestaurantes.com.br/"));
        Assert.Throws<ArgumentException>(() => SitemapBuilder.ResolvePublicOrigin("http://belemrestaurantes.com.br"));
        Assert.Throws<ArgumentException>(() => SitemapBuilder.Build("http://x.br", []));
    }
    [Fact]public void RobotsTxtDeclaresTheSitemapAndProtectsPrivateRegions()
    {
        var text = SitemapBuilder.RobotsTxt("https://belemrestaurantes.com.br");
        using var reader = new StringReader(text);
        var lines = new List<string>();
        string? line; while ((line = reader.ReadLine()) is not null) lines.Add(line);
        Assert.Equal("User-agent: *", lines[0]);
        Assert.Contains("Allow: /", lines);
        foreach (var prefix in SitemapBuilder.DisallowedPrefixes) Assert.Contains("Disallow: " + prefix, lines);
        Assert.Contains("Sitemap: https://belemrestaurantes.com.br/sitemap.xml", lines);
        Assert.True(text.EndsWith('\n'));
    }
}
