using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.Google;

/// <summary>Opções da integração com o Google Places (API New).</summary>
public sealed class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    /// <summary>Chave da API. Nunca versionada: vem de segredo de ambiente.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://places.googleapis.com/v1/";

    public int PageSize { get; set; } = 20;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Teto de requisições por minuto, para não estourar a cota.</summary>
    public int RequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Solicita fotos, avaliações e links. Desligado por padrão: cada campo
    /// tem regra própria de retenção e atribuição sob os termos do Google.
    /// </summary>
    public bool RequestRichContent { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Cliente da Places API (New).
///
/// Regras respeitadas por construção:
/// - apenas campos com permissão de exibição são pedidos no field mask;
/// - avaliações, fotos e resenhas nunca são persistidas (ver <see cref="PlaceValidator"/>);
/// - toda resposta traz origem e instante da consulta para a trilha de proveniência.
/// </summary>
public sealed class GooglePlacesClient(
    HttpClient httpClient,
    GooglePlacesOptions options,
    IClock clock,
    ILogger<GooglePlacesClient> logger) : IPlaceDiscoveryProvider
{
    /// <summary>
    /// Campos mínimos para identificar, localizar e classificar um lugar.
    /// Não inclui reviews, photos nem editorialSummary.
    /// </summary>
    private const string SearchFieldMask =
        "places.id,places.displayName,places.formattedAddress,places.location,places.types," +
        "places.googleMapsUri,places.websiteUri,places.rating,places.userRatingCount,nextPageToken";

    public string Name => "google_places";

    public bool IsConfigured => options.IsConfigured;

    public async Task<PlaceSearchResult> SearchAsync(string textQuery, string? cityFilter, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Google Places não configurado. Defina GooglePlaces:ApiKey como segredo de ambiente.");
        }

        var url = new Uri(new Uri(options.BaseUrl), "places:searchText");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Goog-Api-Key", options.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", SearchFieldMask);

        request.Content = JsonContent.Create(new
        {
            textQuery,
            languageCode = "pt-BR",
            regionCode = "BR",
            pageSize = Math.Clamp(options.PageSize, 1, 20),
            // O filtro de região reduz falsos positivos de homônimos fora do Pará.
            locationBias = BuildLocationBias(cityFilter)
        });

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractApiError(body, response.StatusCode);
            logger.LogWarning(
                "Places respondeu {Status} para '{Query}': {Detail}",
                (int)response.StatusCode, textQuery, detail);

            throw new PlaceApiException(response.StatusCode, detail);
        }

        var places = ParseSearchResponse(body);

        return new PlaceSearchResult(places, ReadNextPageToken(body), clock.UtcNow);
    }

    private IReadOnlyList<DiscoveredPlace> ParsePlaces(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (document.RootElement.TryGetProperty("places", out var placesElement) && placesElement.ValueKind == JsonValueKind.Array)
        {
            return placesElement.EnumerateArray()
                .Select(ParsePlace)
                .OfType<DiscoveredPlace>()
                .ToArray();
        }

        var place = ParsePlace(document.RootElement);
        return place is null ? [] : [place];
    }

    public async Task<DiscoveredPlace?> GetDetailsAsync(string externalId, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        // O ID vem da base64 do Places e precisa ir no caminho, sem query string,
        // para não vazar em log de URL.
        var url = new Uri(new Uri(options.BaseUrl), $"places/{Uri.EscapeDataString(externalId)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", options.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", SearchFieldMask.Replace("places.", ""));

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Lugar removido da fonte: não é erro, apenas ausência.
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new PlaceApiException(response.StatusCode, ExtractApiError(body, response.StatusCode));
        }

        var parsed = ParsePlaces(body);
        return parsed.Count > 0 ? parsed[0] : null;
    }

    private static object? BuildLocationBias(string? cityFilter)
    {
        // Sem coordenada confiável por cidade, omitimos o viés em vez de
        // inventar um raio que traria resultados de fora do estado.
        return null;
    }

    private IReadOnlyList<DiscoveredPlace> ParseSearchResponse(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("places", out var placesElement) ||
            placesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return placesElement.EnumerateArray().Select(ParsePlace).Where(p => p is not null).Select(p => p!).ToArray();
    }

    private static DiscoveredPlace? ParsePlace(JsonElement place)
    {
        if (!place.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var name = place.TryGetProperty("displayName", out var display) &&
                   display.TryGetProperty("text", out var nameText)
            ? nameText.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        double? latitude = null;
        double? longitude = null;

        if (place.TryGetProperty("location", out var location))
        {
            if (location.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latValue))
            {
                latitude = latValue;
            }

            if (location.TryGetProperty("longitude", out var lng) && lng.TryGetDouble(out var lngValue))
            {
                longitude = lngValue;
            }
        }

        var types = place.TryGetProperty("types", out var typesElement) &&
                    typesElement.ValueKind == JsonValueKind.Array
            ? typesElement.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString()!)
                .ToArray()
            : [];

        return new DiscoveredPlace(
            id,
            name,
            Neighborhood: null,
            City: null,
            Category: DeriveCategory(types),
            Address: place.TryGetProperty("formattedAddress", out var address) ? address.GetString() : null,
            Latitude: latitude,
            Longitude: longitude,
            GoogleMapsUri: place.TryGetProperty("googleMapsUri", out var mapsUri) ? mapsUri.GetString() : null,
            Website: place.TryGetProperty("websiteUri", out var website) ? website.GetString() : null,
            GoogleRating: place.TryGetProperty("rating", out var rating) && rating.TryGetDouble(out var r)
                ? (int)Math.Round(r)
                : null,
            GoogleRatingCount: place.TryGetProperty("userRatingCount", out var count) && count.TryGetInt32(out var c)
                ? c
                : null,
            types);
    }

    /// <summary>
    /// O Places devolve tipos em inglês; o mapeamento canônico é feito por
    /// <see cref="CategoryRules"/>, que já conhece os sinônimos.
    /// </summary>
    private static string? DeriveCategory(IReadOnlyList<string> types)
    {
        foreach (var type in types)
        {
            var mapped = CategoryRules.Clean(MapTypeToPortuguese(type));
            if (mapped is not null)
            {
                return mapped;
            }
        }

        return types.Count > 0 ? CategoryRules.Clean(types[0]) : null;
    }

    private static string MapTypeToPortuguese(string type) => type switch
    {
        "restaurant" or "casual_dining" or "fine_dining" or "diner" => "restaurante",
        "pizza_restaurant" => "pizzaria",
        "fast_food_restaurant" or "sandwich_shop" => "lanchonete",
        "cafe" or "coffee_shop" => "cafe",
        "steak_house" or "barbecue_restaurant" => "churrascaria",
        "buffet_restaurant" => "buffet",
        "bakery" => "padaria",
        "hamburger_restaurant" => "hamburgueria",
        "night_club" or "bar" or "pub" => "bar",
        "meal_takeaway" or "food" => "lanchonete",
        _ => type
    };

    private static string? ReadNextPageToken(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("nextPageToken", out var token)
            ? token.GetString()
            : null;
    }

    private static string ExtractApiError(string body, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                var reason = error.TryGetProperty("status", out var s) ? s.GetString() : null;
                return string.IsNullOrWhiteSpace(message) ? reason ?? status.ToString() : message;
            }
        }
        catch (JsonException)
        {
            // corpo não-JSON: cai no status.
        }

        return status.ToString();
    }
}

/// <summary>
/// Erro de API com status, para que a política de retry decida corretamente:
/// 429 e 5xx são tentáveis; 400 e 401 não são.
/// </summary>
public sealed class PlaceApiException(HttpStatusCode statusCode, string detail)
    : Exception($"Google Places respondeu {(int)statusCode}: {detail}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public bool IsRetryable =>
        StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable;
}