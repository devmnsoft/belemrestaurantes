using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class CrawlerParser : ICrawlerParser
{
    private readonly ILogger<CrawlerParser> _logger;

    public CrawlerParser(ILogger<CrawlerParser> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ParseSitemap(string xmlContent, string baseUrl)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return [];

            var ns = root.GetDefaultNamespace();

            // 1. Sitemap Index (aponta para outros sitemaps)
            var sitemaps = root.Elements(ns + "sitemap").Select(s => s.Element(ns + "loc")?.Value.Trim()).Where(v => !string.IsNullOrEmpty(v));
            foreach (var smLoc in sitemaps)
            {
                if (smLoc != null && Uri.TryCreate(smLoc, UriKind.Absolute, out _))
                {
                    urls.Add(smLoc);
                }
            }

            // 2. Urlset (páginas diretas)
            var pages = root.Elements(ns + "url").Select(u => u.Element(ns + "loc")?.Value.Trim()).Where(v => !string.IsNullOrEmpty(v));
            foreach (var pageLoc in pages)
            {
                if (pageLoc != null && Uri.TryCreate(pageLoc, UriKind.Absolute, out _))
                {
                    urls.Add(pageLoc);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Falha ao analisar XML de sitemap para {BaseUrl}: {Msg}", baseUrl, ex.Message);
        }

        return urls.ToList();
    }

    public IReadOnlyList<(string Url, string Title, string? Description)> ParseRssFeed(string xmlContent, string baseUrl)
    {
        var items = new List<(string Url, string Title, string? Description)>();

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return [];

            // Padrão RSS 2.0
            var rssItems = root.Element("channel")?.Elements("item");
            if (rssItems != null)
            {
                foreach (var item in rssItems)
                {
                    var link = item.Element("link")?.Value?.Trim();
                    var title = item.Element("title")?.Value?.Trim() ?? "Sem título";
                    var desc = item.Element("description")?.Value?.Trim();

                    if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out _))
                    {
                        items.Add((link, title, desc));
                    }
                }
            }

            // Padrão Atom Feed
            var atomNs = root.GetDefaultNamespace();
            if (root.Name.LocalName == "feed")
            {
                var entries = root.Elements(atomNs + "entry");
                foreach (var entry in entries)
                {
                    var link = entry.Element(atomNs + "link")?.Attribute("href")?.Value?.Trim();
                    var title = entry.Element(atomNs + "title")?.Value?.Trim() ?? "Sem título";
                    var desc = entry.Element(atomNs + "summary")?.Value?.Trim() ?? entry.Element(atomNs + "content")?.Value?.Trim();

                    if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out _))
                    {
                        items.Add((link, title, desc));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Falha ao analisar Feed RSS/Atom para {BaseUrl}: {Msg}", baseUrl, ex.Message);
        }

        return items;
    }

    public IReadOnlyList<ExtractedRestaurantData> ExtractRestaurants(string htmlContent, string sourceUrl, string defaultCity)
    {
        var list = new List<ExtractedRestaurantData>();

        if (string.IsNullOrWhiteSpace(htmlContent)) return list;

        // 1. Extração estruturada de Schema.org JSON-LD
        var jsonLdMatches = Regex.Matches(htmlContent, @"<script[^>]*type=[""']application\/ld\+json[""'][^>]*>(.*?)<\/script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in jsonLdMatches)
        {
            var jsonText = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(jsonText)) continue;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                ExtractFromSchemaElement(doc.RootElement, list, sourceUrl, defaultCity);
            }
            catch
            {
                // Ignora JSON-LD malformado da página externa
            }
        }

        // 2. Se não encontrou Schema.org específico, mas a página aparenta ser de restaurante paraense, aplica heurística
        if (list.Count == 0 && LooksLikeParanRestaurantPage(htmlContent))
        {
            var heuristic = ExtractHeuristic(htmlContent, sourceUrl, defaultCity);
            if (heuristic != null)
            {
                list.Add(heuristic);
            }
        }

        return list;
    }

    private void ExtractFromSchemaElement(JsonElement element, List<ExtractedRestaurantData> list, string sourceUrl, string defaultCity)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtractFromSchemaElement(item, list, sourceUrl, defaultCity);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        // Suporte a @graph do Yoast / WordPress
        if (element.TryGetProperty("@graph", out var graphEl) && graphEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graphEl.EnumerateArray())
            {
                ExtractFromSchemaElement(item, list, sourceUrl, defaultCity);
            }
            return;
        }

        if (!element.TryGetProperty("@type", out var typeEl)) return;

        var typeStr = typeEl.ToString();
        var isFoodEstablishment = typeStr.Contains("Restaurant", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("FoodEstablishment", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("CafeOrCoffeeShop", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("Bakery", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("BarOrPub", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("FastFoodRestaurant", StringComparison.OrdinalIgnoreCase) ||
                                  typeStr.Contains("IceCreamShop", StringComparison.OrdinalIgnoreCase);

        if (!isFoodEstablishment) return;

        var name = GetStringProperty(element, "name");
        if (string.IsNullOrWhiteSpace(name)) return;

        var data = new ExtractedRestaurantData
        {
            Name = name.Trim(),
            Description = GetStringProperty(element, "description"),
            Telephone = GetStringProperty(element, "telephone"),
            Website = GetStringProperty(element, "url") ?? sourceUrl,
            ServesCuisine = GetStringProperty(element, "servesCuisine"),
            PriceRange = GetStringProperty(element, "priceRange"),
            SourceUrl = sourceUrl,
            City = defaultCity,
            ConfidenceScore = 0.95
        };

        // Endereço
        if (element.TryGetProperty("address", out var addrEl))
        {
            if (addrEl.ValueKind == JsonValueKind.Object)
            {
                var street = GetStringProperty(addrEl, "streetAddress");
                var locality = GetStringProperty(addrEl, "addressLocality");
                var region = GetStringProperty(addrEl, "addressRegion");
                var postal = GetStringProperty(addrEl, "postalCode");

                data.Address = string.Join(", ", new[] { street, locality, region, postal }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(locality)) data.City = locality;
            }
            else if (addrEl.ValueKind == JsonValueKind.String)
            {
                data.Address = addrEl.GetString();
            }
        }

        // Geolocalização
        if (element.TryGetProperty("geo", out var geoEl) && geoEl.ValueKind == JsonValueKind.Object)
        {
            if (geoEl.TryGetProperty("latitude", out var latEl) && latEl.TryGetDouble(out var lat)) data.Latitude = lat;
            if (geoEl.TryGetProperty("longitude", out var lonEl) && lonEl.TryGetDouble(out var lon)) data.Longitude = lon;
        }

        // Redes sociais (sameAs)
        if (element.TryGetProperty("sameAs", out var sameAsEl))
        {
            if (sameAsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in sameAsEl.EnumerateArray())
                {
                    MapSocialLink(data, link.GetString());
                }
            }
            else if (sameAsEl.ValueKind == JsonValueKind.String)
            {
                MapSocialLink(data, sameAsEl.GetString());
            }
        }

        // Imagem oficial com atribuição
        if (element.TryGetProperty("image", out var imgEl))
        {
            if (imgEl.ValueKind == JsonValueKind.String) data.PhotoUrl = imgEl.GetString();
            else if (imgEl.ValueKind == JsonValueKind.Object) data.PhotoUrl = GetStringProperty(imgEl, "url");
            data.PhotoAttribution = $"Fonte oficial: {sourceUrl}";
        }

        list.Add(data);
    }

    private static void MapSocialLink(ExtractedRestaurantData data, string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;

        if (link.Contains("instagram.com/", StringComparison.OrdinalIgnoreCase)) data.Instagram = link.Trim();
        else if (link.Contains("facebook.com/", StringComparison.OrdinalIgnoreCase)) data.Facebook = link.Trim();
        else if (link.Contains("tiktok.com/", StringComparison.OrdinalIgnoreCase)) data.TikTok = link.Trim();
        else if (link.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase)) data.YouTube = link.Trim();
    }

    private static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }
        return null;
    }

    private static bool LooksLikeParanRestaurantPage(string html)
    {
        string[] paraGastronomyKeywords = [
            "restaurante", "culinária paraense", "tacacá", "açaí", "filhote", "maniçoba",
            "pato no tucupi", "vatapá", "tucupi", "belém", "pará", "cardápio", "gastronomia"
        ];

        var count = paraGastronomyKeywords.Count(kw => html.Contains(kw, StringComparison.OrdinalIgnoreCase));
        return count >= 3;
    }

    private static ExtractedRestaurantData? ExtractHeuristic(string html, string sourceUrl, string defaultCity)
    {
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)<\/title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!titleMatch.Success) return null;

        var rawTitle = titleMatch.Groups[1].Value.Trim();
        var cleanName = rawTitle.Split(new[] { '-', '|', '·', ':' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        if (cleanName.Length < 3 || cleanName.Length > 80) return null;

        var data = new ExtractedRestaurantData
        {
            Name = cleanName,
            SourceUrl = sourceUrl,
            City = defaultCity,
            ConfidenceScore = 0.65 // Menor confiança pois foi heurística; necessita de validação por IA e moderação
        };

        // Meta description
        var descMatch = Regex.Match(html, @"<meta[^>]*name=[""']description[""'][^>]*content=[""'](.*?)[""']", RegexOptions.IgnoreCase);
        if (descMatch.Success) data.Description = descMatch.Groups[1].Value.Trim();

        // Telefone paraense (DDD 91, 93 ou 94)
        var phoneMatch = Regex.Match(html, @"\((?:91|93|94)\)\s*(?:9\d{4}|\d{4})[-.\s]?\d{4}");
        if (phoneMatch.Success) data.Telephone = phoneMatch.Value;

        // Links sociais
        var instaMatch = Regex.Match(html, @"https?:\/\/(?:www\.)?instagram\.com\/[a-zA-Z0-9_.]+", RegexOptions.IgnoreCase);
        if (instaMatch.Success) data.Instagram = instaMatch.Value;

        var fbMatch = Regex.Match(html, @"https?:\/\/(?:www\.)?facebook\.com\/[a-zA-Z0-9_.]+", RegexOptions.IgnoreCase);
        if (fbMatch.Success) data.Facebook = fbMatch.Value;

        return data;
    }
}
