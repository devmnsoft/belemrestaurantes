namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Decide se uma imagem pode ser guardada. Site do próprio estabelecimento fica pendente.
/// Commons só entra com licença livre e o nome do lugar no título do arquivo.
/// </summary>
public static class LicensedImageRules
{
    private static readonly string[] BlockedHosts =
    [
        "googleusercontent.com", "ggpht.com", "gstatic.com", "google.com", "googleapis.com",
        "instagram.com", "cdninstagram.com", "fbcdn.net", "facebook.com", "fbsbx.com",
        "tiktokcdn.com", "tiktok.com", "muscdn.com"
    ];

    private static readonly string[] WeakTitleTokens = ["logo", "bandeira", "brasao", "mapa", "coat of arms", "flag of", "locator", "icon"];

    public static bool TryOfficialSiteImage(string? pageUrl, string? imageUrl, out string safeImage)
    {
        safeImage = "";
        if (!TryHttps(pageUrl, out var page) || !TryHttps(imageUrl, out var image)) return false;
        if (!HasImageExtension(image.AbsolutePath)) return false;
        if (IsBlockedHost(image.Host) || IsWikimediaHost(image.Host)) return false;
        if (!SameSite(page.Host, image.Host)) return false;
        safeImage = image.AbsoluteUri;
        return safeImage.Length <= 800;
    }

    public static bool TryFreeLicense(string? shortName, out string code)
    {
        code = "";
        if (string.IsNullOrWhiteSpace(shortName)) return false;
        var text = shortName.ToLowerInvariant();
        if (text.Contains("nc", StringComparison.Ordinal) || text.Contains("nd", StringComparison.Ordinal)) return false;
        if (text.Contains("cc0", StringComparison.Ordinal) || text.Contains("public domain", StringComparison.Ordinal) || text.Contains("pdm", StringComparison.Ordinal))
        {
            code = text.Contains("cc0", StringComparison.Ordinal) ? "cc0" : "pd";
            return true;
        }
        if (text.Contains("by-sa", StringComparison.Ordinal) || text.Contains("by sa", StringComparison.Ordinal))
        {
            code = "cc-by-sa";
            return true;
        }
        if (text.Contains("cc by", StringComparison.Ordinal) || text.Contains("cc-by", StringComparison.Ordinal))
        {
            code = "cc-by";
            return true;
        }
        return false;
    }

    public static bool IsWikimediaUpload(string? url)
    {
        if (!TryHttps(url, out var uri)) return false;
        return uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains("/wikipedia/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool StrongPlaceMatch(string restaurantName, string city, string title, string? description)
    {
        var name = NameNormalizer.Normalize(restaurantName);
        var place = NameNormalizer.Normalize(city);
        if (name.Length < 6 || place.Length < 3) return false;
        var haystack = NameNormalizer.Normalize(title + " " + description);
        var folded = NameNormalizer.RemoveDiacritics(title + " " + (description ?? "")).ToLowerInvariant();
        if (WeakTitleTokens.Any(token => folded.Contains(token, StringComparison.Ordinal))) return false;
        // Belém do Pará e Belém de Lisboa compartilham o nome. Foto com Lisboa não entra.
        if (place == "belem" && (folded.Contains("lisboa", StringComparison.Ordinal) || folded.Contains("portugal", StringComparison.Ordinal) || folded.Contains("jeronimos", StringComparison.Ordinal)))
            return false;
        return haystack.Contains(name, StringComparison.Ordinal) && haystack.Contains(place, StringComparison.Ordinal);
    }

    private static bool IsBlockedHost(string host)
    {
        var key = HostKey(host);
        return BlockedHosts.Any(blocked => key == blocked || key.EndsWith("." + blocked, StringComparison.Ordinal));
    }

    private static bool IsWikimediaHost(string host)
    {
        var key = HostKey(host);
        return key is "wikimedia.org" or "wikipedia.org" || key.EndsWith(".wikimedia.org", StringComparison.Ordinal) || key.EndsWith(".wikipedia.org", StringComparison.Ordinal);
    }

    private static bool SameSite(string pageHost, string imageHost)
    {
        var page = HostKey(pageHost);
        var image = HostKey(imageHost);
        return page == image || image.EndsWith("." + page, StringComparison.Ordinal) || page.EndsWith("." + image, StringComparison.Ordinal);
    }

    private static string HostKey(string host)
    {
        var key = host.Trim().TrimEnd('.').ToLowerInvariant();
        return key.StartsWith("www.", StringComparison.Ordinal) ? key[4..] : key;
    }

    private static bool HasImageExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static bool TryHttps(string? value, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri!)
            && uri.Scheme == "https"
            && string.IsNullOrEmpty(uri.UserInfo);
    }
}
