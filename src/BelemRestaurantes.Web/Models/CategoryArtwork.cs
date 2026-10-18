using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.Web.Models;

public sealed record CategoryArtwork(string Key, string Label)
{
    public static CategoryArtwork For(string? category)
    {
        var name = NameNormalizer.RemoveDiacritics(category ?? "").ToLowerInvariant();
        if (name.Contains("pizza")) return new("pizza", "Pizzaria");
        if (name.Contains("acai")) return new("acai", "Açaí");
        if (name.Contains("cafe") || name.Contains("cafeter")) return new("coffee", "Cafeteria");
        if (name.Contains("padar") || name.Contains("panific")) return new("bread", "Padaria");
        if (name.Contains("sorvet") || name.Contains("gelat")) return new("icecream", "Sorveteria");
        if (name.Contains("lanch") || name.Contains("hamburg")) return new("snack", "Lanchonete");
        if (name.Contains("peixe") || name.Contains("pescad") || name.Contains("frutos do mar")) return new("fish", "Peixes e frutos do mar");
        if (name.Contains("bar") || name.Contains("pub")) return new("bar", "Bar");
        return new("plate", "Restaurante");
    }
}
