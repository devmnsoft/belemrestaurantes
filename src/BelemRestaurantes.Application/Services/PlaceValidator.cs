using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Services;

/// <summary>Veredito sobre um lugar retornado por uma fonte externa.</summary>
public sealed record PlaceValidationResult(bool IsAcceptable, string Reason)
{
    public static PlaceValidationResult Accept() => new(true, "ok");

    public static PlaceValidationResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// Portão de entrada de dados externos. Centraliza as regras de publicabilidade e
/// de licenciamento que a API de Places impõe, para que nenhum outro caminho
/// (IA, conector social, script manual) as contorne.
/// </summary>
public static class PlaceValidator
{
    /// <summary>Campos que o Google permite armazenar e exibir, por Place ID.</summary>
    private static readonly HashSet<string> AllowedPersistedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "displayName", "formattedAddress", "location", "types",
        "rating", "userRatingCount", "googleMapsUri", "websiteUri", "nationalPhoneNumber", "regularOpeningHours"
    };

    /// <summary>
    /// Campos que o Google proíbe de persistir. Tê-los aqui é documentação
    /// executável: o teste correspondente falha se alguém incluir um deles no DTO.
    /// </summary>
    public static IReadOnlySet<string> ForbiddenPersistedFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reviews", "reviewText", "editorialSummary", "photos", "photoUrls", "plusCodes"
        };

    public static PlaceValidationResult Validate(DiscoveredPlace place)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (string.IsNullOrWhiteSpace(place.ExternalId))
        {
            return PlaceValidationResult.Reject("Sem identificador externo: impossível deduplicar.");
        }

        if (string.IsNullOrWhiteSpace(place.Name))
        {
            return PlaceValidationResult.Reject("Nome vazio.");
        }

        if (place.Name.Length > 160)
        {
            return PlaceValidationResult.Reject("Nome acima do limite da coluna.");
        }

        if (place.NormalizedName.Length == 0)
        {
            return PlaceValidationResult.Reject("Nome sem letras ou dígitos após normalização.");
        }

        // Sem município não dá para montar a busca por cidade do site.
        if (place.ResolvedCity.Length == 0)
        {
            return PlaceValidationResult.Reject("Município ausente.");
        }

        if (place.State is not null && NameNormalizer.Normalize(place.State) is not ("para" or "pa"))
            return PlaceValidationResult.Reject("Estado informado pela fonte fora do Pará.");

        // Localização precisa estar no Pará; um resultado fora do estado indica
        // casamento errado na consulta de texto e deve ser descartado.
        if (place.Latitude is { } lat && (lat < -10.0 || lat > 2.6))
        {
            return PlaceValidationResult.Reject($"Latitude {lat:F3} fora do Pará.");
        }

        if (place.Longitude is { } lng && (lng < -74.5 || lng > -46.0))
        {
            return PlaceValidationResult.Reject($"Longitude {lng:F3} fora do Pará.");
        }

        // Links precisam ser https: o site só emite href com esquema confiável.
        if (place.Website is not null && !SocialLinkRules.TryValidate(place.Website, out _))
        {
            return PlaceValidationResult.Reject("Site oficial com esquema não permitido.");
        }

        // Nota fora da escala do Google significa dado corrompido.
        if (place.GoogleRating is { } rating && (rating < 0 || rating > 5))
        {
            return PlaceValidationResult.Reject($"Nota {rating} fora da escala 0-5.");
        }

        if (place.GoogleRatingCount is < 0)
        {
            return PlaceValidationResult.Reject("Contagem de avaliações negativa.");
        }

        return PlaceValidationResult.Accept();
    }

    /// <summary>
    /// Só a lista branca pode chegar ao <c>INSERT</c>. Usado pela proyección de
    /// campos no repositório e coberto por teste.
    /// </summary>
    public static bool IsPersistable(string fieldName) => AllowedPersistedFields.Contains(fieldName);

    public static IReadOnlyCollection<string> AllowedPersistedFieldNames { get; } = AllowedPersistedFields;
}
