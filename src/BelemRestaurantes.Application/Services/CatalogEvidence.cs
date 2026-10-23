using System.Globalization;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Application.Services;

public static class FieldStates
{
    public const string Informed = "informed";
    public const string Verified = "verified";
    public const string NotLocated = "not_located";
    public const string Conflict = "conflict";
    public const string Pending = "pending";

    public static string Label(string state) => state switch
    {
        Informed => "Informado pela fonte",
        Verified => "Verificado",
        Conflict => "Informação conflitante",
        Pending => "Aguardando confirmação",
        _ => "Ainda não localizado"
    };
}

/// <summary>Prato servido e receita publicada são fatos diferentes.</summary>
public static class DishRecipe
{
    public const string RestaurantDish = "restaurant_dish";
    public const string PublishedRecipe = "published_recipe";
    public const string TraditionalSimilar = "traditional_similar";

    public static string Classify(bool offeredByPlace, bool recipePublishedByPlaceOrChef, bool similarHomeVersion)
    {
        if (recipePublishedByPlaceOrChef && offeredByPlace) return PublishedRecipe;
        if (recipePublishedByPlaceOrChef) return PublishedRecipe;
        if (offeredByPlace) return RestaurantDish;
        if (similarHomeVersion) return TraditionalSimilar;
        return RestaurantDish;
    }

    public static string VisitorLabel(string kind) => kind switch
    {
        PublishedRecipe => "Receita divulgada pela fonte identificada. Não completa quantidades que a fonte não publicou.",
        TraditionalSimilar => "Receita tradicional ou versão caseira semelhante. Não é a receita oficial do restaurante.",
        _ => "Prato associado ao estabelecimento. Isso não publica a receita."
    };

    public static bool CanScalePortions(IEnumerable<string> ingredients) =>
        ingredients.Any(line => Regex.IsMatch(line, @"\d", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)));
}

public sealed record ObservedOffer(string Product, string Seller, decimal Price, string Unit, decimal Quantity, DateTimeOffset ObservedAt, string SourceUrl, decimal? Freight = null);

public static class OfferMath
{
    public static string Summarize(IReadOnlyList<ObservedOffer> offers, string unit)
    {
        var same = offers.Where(o => string.Equals(o.Unit, unit, StringComparison.OrdinalIgnoreCase) && o.Quantity > 0).ToArray();
        if (same.Length == 0) return "Não há oferta comparável com unidade e data.";
        if (same.Length == 1)
        {
            var one = same[0];
            var freight = one.Freight is null ? "Frete não informado." : "Frete observado à parte: " + one.Freight.Value.ToString("0.00", CultureInfo.InvariantCulture) + ".";
            return "Preço observado: " + one.Price.ToString("0.00", CultureInfo.InvariantCulture) + " por " + one.Quantity.ToString("0.##", CultureInfo.InvariantCulture) + " " + one.Unit + " em " + one.ObservedAt.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) + " (" + one.Seller + "). Não é média. " + freight;
        }
        var per = same.Select(o => o.Price / o.Quantity).ToArray();
        var average = per.Average();
        return "Média de " + same.Length + " ofertas equivalentes, normalizada por " + unit + ": " + average.ToString("0.00", CultureInfo.InvariantCulture) + ". Amostra de " + same.Min(o => o.ObservedAt).ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) + " a " + same.Max(o => o.ObservedAt).ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")) + ". Frete fica fora da média.";
    }

    public static string? RecipeCost(IReadOnlyList<(string Name, decimal? UsedAmount, decimal? PackagePrice, bool Priced)> lines, int portions)
    {
        if (portions < 1 || lines.Count == 0) return null;
        var priced = lines.Where(l => l.Priced && l.UsedAmount is not null && l.PackagePrice is not null).ToArray();
        var missing = lines.Where(l => !l.Priced).Select(l => l.Name).ToArray();
        if (priced.Length == 0) return "Não há preço para estimar o custo. Itens sem preço: " + string.Join(", ", lines.Select(l => l.Name)) + ".";
        var proportional = priced.Sum(l => l.PackagePrice!.Value * l.UsedAmount!.Value);
        var text = "Custo proporcional dos ingredientes com preço, para " + portions + " porções: " + proportional.ToString("0.00", CultureInfo.InvariantCulture) + ". Isso não é o desembolso de comprar as embalagens inteiras.";
        if (missing.Length > 0) text += " Sem preço, e por isso fora da conta: " + string.Join(", ", missing) + ".";
        return text;
    }
}

public static class CuiaPlaceQuestion
{
    public static bool NeedsExternal(string prompt)
    {
        var text = Fold(prompt);
        return Regex.IsMatch(text, @"\b(horarios?|cardapio|pratos|receita oficial|videos?|onde comprar|ingredientes desta|quanto custa|mais barata|nao esteja no catalogo|ainda nao esteja)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80));
    }

    public static bool IsHours(string prompt) => Regex.IsMatch(Fold(prompt), @"\bhorarios?\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static string? NameHint(string prompt, IEnumerable<string>? previous = null)
    {
        var fromPrompt = ExtractName(prompt);
        if (fromPrompt is not null) return fromPrompt;
        var folded = Fold(prompt);
        var refersBack = Regex.IsMatch(folded, @"\b(esse|essa|desse|dessa|deste|desta|dali|mesmo|mesma|desses|disso|este|esta|quanto custa|mais barata)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!refersBack) return null;
        foreach (var text in previous ?? [])
        {
            var name = ExtractName(text);
            if (name is not null) return name;
        }
        return null;
    }

    private static string? ExtractName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text, @"(?:hor[aá]rios?|pratos|receita oficial|card[aá]pio|telefone)\s+(?:d[oa]|de)\s+(.+?)(?:\s+em\s+|\?|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80));
        if (!match.Success) return null;
        var name = match.Groups[1].Value.Trim().Trim('"', '“', '”');
        if (name.Length is < 3 or > 120) return null;
        if (Regex.IsMatch(Fold(name), @"^(algum|alguma|alguns|alguns|esse|essa|este|esta|um|uma|esses|essas)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) return null;
        return name;
    }

    public static string HoursGap(string name, string city, string? neighborhood)
    {
        var where = string.IsNullOrWhiteSpace(neighborhood) ? city : neighborhood + ", " + city;
        return "**" + name + "** tem ficha em " + where + ". O horário de funcionamento ainda não foi localizado em fonte suficiente. Não marco aberto agora.";
    }

    public static string Trace(string prompt, int catalogHits, bool searchRequested, string? searchStatus, int sources, string? provider)
    {
        var intent = IsHours(prompt) ? "horario" : NeedsExternal(prompt) ? "pesquisa" : "catalogo";
        var decision = searchRequested ? "consultar_externo" : "somente_catalogo";
        var safeProvider = string.IsNullOrWhiteSpace(provider) ? "nenhum" : "configurado";
        return "intencao=" + intent + ";catalogo=" + catalogHits + ";decisao=" + decision + ";estado=" + (searchStatus ?? "not_requested") + ";fontes=" + sources + ";conector=" + safeProvider;
    }

    private static string Fold(string value) => BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(value).ToLowerInvariant();
}

public static class VideoReferenceRules
{
    public static bool Canonical(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort
        && (uri.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.tiktok.com", StringComparison.OrdinalIgnoreCase));
}
