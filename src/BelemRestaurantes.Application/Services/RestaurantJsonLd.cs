using System;
using System.Collections.Generic;
using System.Globalization;
using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Services;

/// <summary>Sprint 77 — JSON-LD (schema.org/Restaurant) da ficha pública do estabelecimento.
/// Função pura: recebe a <see cref="RestaurantView"/> já montada pelo controlador e devolve o
/// grafo que a view serializa, testável sem banco nem HTTP.
/// Regra de honestidade (a mesma do portal): o schema só carrega o que a ficha realmente tem —
/// telefone, foto, endereço, website e coordenada só viram chave quando existem na ficha.
/// Nada de valor padrão inventado para completar o card.</summary>
public static class RestaurantJsonLd
{
    /// <summary>URL pública da ficha: mesma rota que o sitemap e os links internos usam.</summary>
    public static string PageUrl(long id, string origin) =>
        string.Concat(origin, "/estabelecimento/", id.ToString(CultureInfo.InvariantCulture));

    /// <summary>URL absoluta da foto da ficha, ou null quando a ficha não tem foto pública.
    /// Aceita as duas formas que a vitrine aceita: caminho relativo sob /media/ (ganha a origem)
    /// ou URL absoluta HTTPS. Qualquer outra forma é tratada como "sem foto", nunca adivinhada.</summary>
    public static string? AbsolutePhotoUrl(string? photoUrl, string origin)
    {
        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            return null;
        }
        if (photoUrl.StartsWith("/media/", StringComparison.Ordinal))
        {
            return string.Concat(origin, photoUrl);
        }
        return Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? photoUrl
            : null;
    }

    public static Dictionary<string, object?> Build(RestaurantView restaurant, string origin)
    {
        ArgumentNullException.ThrowIfNull(restaurant);
        var schema = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Restaurant",
            ["name"] = restaurant.Name,
            ["url"] = PageUrl(restaurant.Id, origin),
        };

        var image = AbsolutePhotoUrl(restaurant.PhotoUrl, origin);
        if (image is not null)
        {
            schema["image"] = image;
        }

        if (!string.IsNullOrWhiteSpace(restaurant.Address))
        {
            schema["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = restaurant.Address,
                ["addressLocality"] = restaurant.City,
            };
        }

        if (!string.IsNullOrWhiteSpace(restaurant.Phone))
        {
            schema["telephone"] = restaurant.Phone;
        }

        if (!string.IsNullOrWhiteSpace(restaurant.Website))
        {
            schema["sameAs"] = restaurant.Website;
        }

        if (IsValidCoordinate(restaurant.Latitude, restaurant.Longitude))
        {
            schema["geo"] = new Dictionary<string, object?>
            {
                ["@type"] = "GeoCoordinates",
                ["latitude"] = restaurant.Latitude!.Value.ToString(CultureInfo.InvariantCulture),
                ["longitude"] = restaurant.Longitude!.Value.ToString(CultureInfo.InvariantCulture),
            };
        }

        return schema;
    }

    private static bool IsValidCoordinate(double? latitude, double? longitude) =>
        latitude.HasValue && longitude.HasValue
        && Math.Abs(latitude.Value) <= 90 && Math.Abs(longitude.Value) <= 180;
}
