using System;
using System.Net;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Enums;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class CuiaCrawlerTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]       // Loopback IPv4
    [InlineData("127.0.0.5", true)]       // Loopback range IPv4
    [InlineData("::1", true)]             // Loopback IPv6
    [InlineData("10.0.1.25", true)]       // RFC 1918 (Classe A)
    [InlineData("172.16.0.1", true)]      // RFC 1918 (Classe B)
    [InlineData("172.31.255.254", true)]  // RFC 1918 (Classe B)
    [InlineData("192.168.0.1", true)]     // RFC 1918 (Classe C)
    [InlineData("192.168.100.50", true)]  // RFC 1918 (Classe C)
    [InlineData("169.254.169.254", true)] // Link-Local / Cloud Metadata (AWS/GCP/Azure)
    [InlineData("169.254.1.1", true)]     // Link-Local
    [InlineData("100.64.0.1", true)]      // Carrier-Grade NAT (RFC 6598)
    [InlineData("0.0.0.0", true)]         // Any
    [InlineData("8.8.8.8", false)]        // IP Público (Google DNS)
    [InlineData("177.136.250.10", false)] // IP Público (Provedor Brasileiro)
    [InlineData("200.221.2.45", false)]   // IP Público (UOL Brasil)
    public void SafeHttpClient_IsDisallowedIp_EnforcesStrictSsrfProtection(string ipString, bool expectedDisallowed)
    {
        var ip = IPAddress.Parse(ipString);
        var isDisallowed = SafeHttpClient.IsDisallowedIp(ip);
        Assert.Equal(expectedDisallowed, isDisallowed);
    }

    [Fact]
    public void CrawlerParser_ParseSitemap_ExtractsUrlsCorrectly()
    {
        var parser = new CrawlerParser(NullLogger<CrawlerParser>.Instance);
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url>
                <loc>https://exemplo.com.br/restaurantes/remanso-do-bosque</loc>
                <lastmod>2026-10-15</lastmod>
              </url>
              <url>
                <loc>https://exemplo.com.br/restaurantes/point-do-acai</loc>
                <lastmod>2026-10-16</lastmod>
              </url>
            </urlset>
            """;

        var urls = parser.ParseSitemap(xml, "https://exemplo.com.br");

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://exemplo.com.br/restaurantes/remanso-do-bosque", urls);
        Assert.Contains("https://exemplo.com.br/restaurantes/point-do-acai", urls);
    }

    [Fact]
    public void CrawlerParser_ParseRssFeed_ExtractsItemsCorrectly()
    {
        var parser = new CrawlerParser(NullLogger<CrawlerParser>.Instance);
        var rss = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>Guia Culinário do Pará</title>
                <link>https://guiapara.com.br</link>
                <item>
                  <title>Tacacá da Dona Maria</title>
                  <link>https://guiapara.com.br/tacaca-dona-maria</link>
                  <description>O melhor tacacá com tucupi fervente e jambu fresco.</description>
                </item>
              </channel>
            </rss>
            """;

        var items = parser.ParseRssFeed(rss, "https://guiapara.com.br");

        Assert.Single(items);
        Assert.Equal("Tacacá da Dona Maria", items[0].Title);
        Assert.Equal("https://guiapara.com.br/tacaca-dona-maria", items[0].Url);
    }

    [Fact]
    public void CrawlerParser_ExtractRestaurants_ParsesSchemaOrgJsonLd()
    {
        var parser = new CrawlerParser(NullLogger<CrawlerParser>.Instance);
        var html = """
            <!DOCTYPE html>
            <html>
            <head>
              <title>Lá em Casa — Cozinha Paraense Autêntica</title>
              <script type="application/ld+json">
              {
                "@context": "https://schema.org",
                "@type": "Restaurant",
                "name": "Restaurante Lá em Casa",
                "description": "Pratos típicos paraenses na Estação das Docas.",
                "telephone": "(91) 3212-5588",
                "servesCuisine": "Regional Paraense",
                "priceRange": "$$",
                "address": {
                  "@type": "PostalAddress",
                  "streetAddress": "Av. Boulevard Castilhos França, Estação das Docas, Galpão 2",
                  "addressLocality": "Belém",
                  "addressRegion": "PA",
                  "postalCode": "66010-020"
                },
                "geo": {
                  "@type": "GeoCoordinates",
                  "latitude": -1.4503,
                  "longitude": -48.5020
                },
                "sameAs": [
                  "https://www.instagram.com/laemcasarestaurante/",
                  "https://www.facebook.com/laemcasabelem"
                ]
              }
              </script>
            </head>
            <body>
              <h1>Restaurante Lá em Casa</h1>
            </body>
            </html>
            """;

        var list = parser.ExtractRestaurants(html, "https://laemcasa.com.br", "Belém");

        Assert.Single(list);
        var r = list[0];
        Assert.Equal("Restaurante Lá em Casa", r.Name);
        Assert.Equal("Belém", r.City);
        Assert.Equal("(91) 3212-5588", r.Telephone);
        Assert.Equal("Regional Paraense", r.ServesCuisine);
        Assert.Equal("https://www.instagram.com/laemcasarestaurante/", r.Instagram);
        Assert.Equal("https://www.facebook.com/laemcasabelem", r.Facebook);
        Assert.Equal(-1.4503, r.Latitude);
        Assert.Equal(-48.5020, r.Longitude);
        Assert.True(r.ConfidenceScore >= 0.9);
    }

    [Fact]
    public void CrawlerDeduplication_DetectsIdenticalNormalizedNames()
    {
        var normalized = NameNormalizer.Normalize("Restaurante Boi de Ouro");
        var candidates = new List<DeduplicationCandidate>
        {
            new(101, normalized, "Belém", "place_101")
        };

        var result = DeduplicationService.Compare("crawler_999", "Boi de Ouro", "Belém", candidates);

        // Nomes normalizados coincidem em Belém: suspeita provável de duplicata
        Assert.Equal(MatchOutcome.ProbableDuplicate, result.Outcome);
        Assert.Equal(101, result.MatchedRestaurantId);
        Assert.True(result.Confidence >= 0.9);
    }
}
