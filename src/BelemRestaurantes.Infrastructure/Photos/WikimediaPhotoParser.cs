using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.Infrastructure.Photos;

public static partial class WikimediaPhotoParser
{
    public static IReadOnlyList<LicensedPhotoCandidate> Read(string json, string restaurantName, string city)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
            return [];

        var found = new List<LicensedPhotoCandidate>();
        foreach (var page in pages.EnumerateObject())
        {
            var item = page.Value;
            var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
            if (!item.TryGetProperty("imageinfo", out var infoEl) || infoEl.ValueKind != JsonValueKind.Array) continue;
            var info = infoEl.EnumerateArray().FirstOrDefault();
            if (info.ValueKind == JsonValueKind.Undefined) continue;
            var mime = info.TryGetProperty("mime", out var mimeEl) ? mimeEl.GetString() : null;
            if (mime is not ("image/jpeg" or "image/png" or "image/webp")) continue;
            if (info.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt32(out var size) && (size < 20_000 || size > 20_000_000)) continue;
            var url = info.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            var thumb = info.TryGetProperty("thumburl", out var thumbEl) ? thumbEl.GetString() : null;
            if (!LicensedImageRules.IsWikimediaUpload(url)) continue;
            if (url!.Length > 800) url = LicensedImageRules.IsWikimediaUpload(thumb) && thumb!.Length <= 800 ? thumb : null;
            if (url is null) continue;
            if (thumb is not null && !LicensedImageRules.IsWikimediaUpload(thumb)) thumb = null;

            var meta = info.TryGetProperty("extmetadata", out var metaEl) ? metaEl : default;
            var licenseName = Meta(meta, "LicenseShortName");
            if (!LicensedImageRules.TryFreeLicense(licenseName, out var license)) continue;
            var description = Meta(meta, "ImageDescription");
            var artist = Meta(meta, "Artist");
            if (string.IsNullOrWhiteSpace(artist)) artist = "autor não informado";
            var strong = LicensedImageRules.StrongPlaceMatch(restaurantName, city, title, description);
            var credit = $"{Trim(artist, 120)} · {Trim(licenseName, 40)} · {Trim(title, 180)}";
            var source = "https://commons.wikimedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
            if (source.Length > 800) continue;
            found.Add(new LicensedPhotoCandidate(url, thumb, source, Trim(credit, 480), license, strong));
            if (found.Count == 3) break;
        }
        return found;
    }

    private static string Meta(JsonElement meta, string name)
    {
        if (meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty(name, out var node)) return "";
        var raw = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("value", out var value) ? value.GetString() : node.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var plain = Tag().Replace(raw, " ");
        return WebUtility.HtmlDecode(plain).Replace('\n', ' ').Trim();
    }

    private static string Trim(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}
