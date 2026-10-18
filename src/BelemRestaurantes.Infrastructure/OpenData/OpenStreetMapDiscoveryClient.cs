using System.Net;
using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;

namespace BelemRestaurantes.Infrastructure.OpenData;

/// <summary>Importação limitada por município, com cache durável de 24h.
/// Dados ODbL isolados em open_data_snapshots e disponíveis para exportação.</summary>
public sealed class OpenStreetMapDiscoveryClient(HttpClient http, IOpenDataCache cache, IClock clock,
    ILogger<OpenStreetMapDiscoveryClient> logger, IReadOnlyList<Uri>? fallbackEndpoints = null) : IPlaceDiscoveryProvider
{
    public static readonly Uri DefaultEndpoint = new("https://overpass-api.de/api/interpreter");
    public static readonly Uri DefaultFallbackEndpoint = new("https://overpass.private.coffee/api/interpreter");
    public const int MaxElements = 2000;
    private const string Amenities = "restaurant|cafe|fast_food|bar|pub|ice_cream|food_court|marketplace";
    private const string Shops = "bakery|pastry|confectionery|deli|cheese|seafood|greengrocer|coffee|tea|chocolate|ice_cream|food";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Uri[] endpoints = BuildEndpoints(http.BaseAddress ?? DefaultEndpoint, fallbackEndpoints);
    // Protegido pelo gate compartilhado entre municípios e categorias.
    private DiscoveryProviderUnavailableException? unavailable;
    public string Name => "openstreetmap";
    public bool IsConfigured => true;

    public Task<PlaceSearchResult> SearchAsync(string textQuery, string? cityFilter, CancellationToken ct)
        => SearchCoreAsync(textQuery, cityFilter, false, ct);

    /// <summary>Atualização pontual e explícita pelo operador; ciclos comuns respeitam o cache de 24h.</summary>
    public Task<PlaceSearchResult> RefreshAsync(string city, CancellationToken ct)
        => SearchCoreAsync("regional", city, true, ct);

    private async Task<PlaceSearchResult> SearchCoreAsync(string textQuery, string? cityFilter, bool refresh, CancellationToken ct)
    {
        var city = ParaOfficialCatalog.Municipalities.FirstOrDefault(c =>
            c.NormalizedName == NameNormalizer.Normalize(cityFilter))?.Name
            ?? throw new ArgumentException("Município fora do catálogo oficial do Pará.", nameof(cityFilter));
        await gate.WaitAsync(ct);
        try
        {
            var snapshot = await cache.GetAsync(city, ct);
            if (refresh || snapshot is null || clock.UtcNow - snapshot.FetchedAt >= TimeSpan.FromHours(24))
            {
                var query = BuildQuery(city);
                var payload = await FetchAsync(query, city, ct);
                if (payload.Length > 4_000_000) throw new InvalidOperationException("Resposta da fonte acima do limite de importação.");
                // Valida antes de persistir; Overpass pode devolver um JSON parcial com remark.
                _ = Parse(payload, city);
                snapshot = new(city, payload, clock.UtcNow);
                await cache.SaveAsync(snapshot, ct);
                logger.LogInformation("OpenStreetMap: consulta real em {City}; snapshot ODbL salvo, atualização externa após 24h.", city);
            }
            else logger.LogInformation("OpenStreetMap: usando snapshot de {FetchedAt} para {City}; nenhuma chamada externa neste job.", snapshot.FetchedAt, city);
            var all = Parse(snapshot.Payload, city);
            var requested = NameNormalizer.RemoveDiacritics(textQuery.Split(" em ", 2, StringSplitOptions.None)[0]).ToLowerInvariant().Trim();
            IReadOnlyList<DiscoveredPlace> places = requested switch
            {
                "regional" or "" => all,
                "restaurante" => all.Where(p => p.Category == "Restaurante").ToArray(),
                "pizzaria" => all.Where(p => p.Category == "Pizzaria").ToArray(),
                "cafe" or "cafeteria" => all.Where(p => p.Category == "Cafeteria").ToArray(),
                "bar" => all.Where(p => p.Category == "Bar").ToArray(),
                "lanchonete" => all.Where(p => p.Category == "Lanchonete").ToArray(),
                "feira" or "mercado" => all.Where(p => p.Category == "Feira e mercado").ToArray(),
                "acai" => all.Where(p => p.Category == "Açaí").ToArray(),
                _ => all.Where(p => NameNormalizer.RemoveDiacritics(p.Name + " " + p.Category + " " + string.Join(' ', p.Types)).Contains(requested, StringComparison.OrdinalIgnoreCase)).ToArray()
            };
            return new(places, null, snapshot.FetchedAt);
        }
        finally { gate.Release(); }
    }

    public Task<DiscoveredPlace?> GetDetailsAsync(string externalId, CancellationToken ct) => Task.FromResult<DiscoveredPlace?>(null);

    private async Task<string> FetchAsync(string query, string city, CancellationToken ct)
    {
        if (unavailable is { } paused && clock.UtcNow < paused.RetryAt)
            throw new DiscoveryProviderUnavailableException(Name, paused.RetryAt,
                $"OpenStreetMap em pausa após falhas externas; consulta de {city} adiada. {paused.Message}");
        const int maxAttempts = 3;
        var retryAt = clock.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            var endpoint = endpoints[(attempt - 1) % endpoints.Length];
            TimeSpan? retryAfter = null;
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
                using var response = await http.PostAsync(endpoint, content, ct);
                retryAfter = response.Headers.RetryAfter?.Delta;
                if (response.Headers.RetryAfter?.Date is { } headerRetryAt)
                    retryAfter = headerRetryAt - clock.UtcNow;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < maxAttempts && IsTransient(ex))
            {
                if (retryAfter is { } wait && clock.UtcNow.Add(wait) > retryAt)
                    retryAt = clock.UtcNow.Add(wait);
                // Esperas longas pertencem à fila durável, não ao prazo de execução do job.
                if (retryAfter > TimeSpan.FromSeconds(30)) throw Pause(city, attempt, endpoint, retryAt, ex);
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                if (retryAfter > delay) delay = retryAfter.Value;
                logger.LogWarning("OpenStreetMap: falha temporária em {City} via {Endpoint}, tentativa {Attempt}/{MaxAttempts}; nova tentativa via {NextEndpoint} em {Delay:F1}s. {Reason}",
                    city, endpoint.Host, attempt, maxAttempts, endpoints[attempt % endpoints.Length].Host, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
            {
                if (retryAfter is { } wait && clock.UtcNow.Add(wait) > retryAt)
                    retryAt = clock.UtcNow.Add(wait);
                throw Pause(city, attempt, endpoint, retryAt, ex);
            }
        }
    }

    private DiscoveryProviderUnavailableException Pause(string city, int attempt, Uri endpoint, DateTimeOffset retryAt, Exception error)
    {
        var minimum = clock.UtcNow.AddMinutes(2);
        unavailable = new(Name, retryAt > minimum ? retryAt : minimum,
            $"OpenStreetMap indisponível para {city} após {attempt} tentativas; último servidor: {endpoint.Host}. {error.Message}", error);
        return unavailable;
    }

    private static Uri[] BuildEndpoints(Uri primary, IReadOnlyList<Uri>? fallbacks)
    {
        // Instância própria não envia consultas a terceiros sem configuração explícita.
        fallbacks ??= primary == DefaultEndpoint ? [DefaultFallbackEndpoint] : [];
        var result = new[] { primary }.Concat(fallbacks).Distinct().ToArray();
        if (result.Any(uri => !uri.IsAbsoluteUri || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidOperationException("Endpoints OpenData exigem HTTPS sem credenciais na URL.");
        return result;
    }

    private static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
        OperationCanceledException => true, // Timeout HTTP; cancelamento do chamador é excluído pelo filtro.
        _ => false
    };

    public static string BuildQuery(string city)
    {
        var official = ParaOfficialCatalog.Municipalities.FirstOrDefault(c => c.NormalizedName == NameNormalizer.Normalize(city))
            ?? throw new ArgumentException("Município fora do catálogo oficial do Pará.", nameof(city));
        var name = JsonSerializer.Serialize(official.Name, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return "[out:json][timeout:25][maxsize:33554432];area[\"ISO3166-2\"=\"BR-PA\"]->.state;" +
            "rel(area.state)[\"boundary\"=\"administrative\"][\"admin_level\"=\"8\"][\"name\"=" + name +
            "];map_to_area->.city;(nwr(area.city)[\"amenity\"~\"^(" + Amenities + ")$\"][\"name\"];" +
            "nwr(area.city)[\"shop\"~\"^(" + Shops + ")$\"][\"name\"];);out center tags " + MaxElements + ";";
    }

    public static IReadOnlyList<DiscoveredPlace> Parse(string payload, string city)
    {
        using var json = JsonDocument.Parse(payload);
        if (json.RootElement.TryGetProperty("remark", out _)) throw new InvalidOperationException("A fonte devolveu um resultado parcial; a consulta será repetida após backoff.");
        if (!json.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Resposta OpenStreetMap sem lista de elementos.");
        var result = new List<DiscoveredPlace>();
        if (elements.GetArrayLength() > MaxElements) throw new InvalidOperationException("Snapshot acima do limite de estabelecimentos por município.");
        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags)) continue;
            string? Tag(string key) => tags.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!Amenities.Split('|').Contains(Tag("amenity")) && !Shops.Split('|').Contains(Tag("shop"))) continue;
            if (Tag("disused") == "yes" || Tag("abandoned") == "yes" || Tag("demolished") == "yes") continue;
            var name = Tag("name");
            var type = element.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || type is not ("node" or "way" or "relation") || !element.TryGetProperty("id", out var id)) continue;
            var coordinates = element.TryGetProperty("center", out var center) ? center : element;
            double? Number(string key) => coordinates.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) ? number : null;
            var lat = Number("lat"); var lng = Number("lon");
            if (lat is null || lng is null || !double.IsFinite(lat.Value) || !double.IsFinite(lng.Value)
                || lat is < -10 or > 2.6 || lng is < -74.5 or > -46) continue;
            // O município vem do polígono administrativo da consulta, não do raio aproximado.
            if (Tag("addr:city") is { } sourceCity && NameNormalizer.Normalize(sourceCity) != NameNormalizer.Normalize(city)) continue;
            var category = Classify(name, Tag("amenity"), Tag("shop"), Tag("cuisine"));
            string? SafeLink(string? value) => SocialLinkRules.TryValidate(value, out var safe) ? safe : null;
            var address = string.Join(", ", new[] { Tag("addr:street"), Tag("addr:housenumber") }.Where(v => !string.IsNullOrWhiteSpace(v)));
            result.Add(new($"osm:{type}:{id.GetRawText()}", name, Tag("addr:suburb") ?? Tag("addr:neighbourhood"), city,
                category, string.IsNullOrWhiteSpace(address) ? null : address, lat, lng,
                $"https://www.openstreetmap.org/{type}/{id.GetRawText()}", SafeLink(Tag("contact:website") ?? Tag("website")), null, null, [Tag("amenity") ?? "", Tag("shop") ?? "", Tag("cuisine") ?? ""])
            {
                Phone = Tag("contact:phone") ?? Tag("phone"), State = "PA",
                Instagram = SafeLink(Tag("contact:instagram")), Facebook = SafeLink(Tag("contact:facebook"))
            });
        }
        return result.DistinctBy(p => p.ExternalId).ToArray();
    }

    private static string Classify(string name, string? amenity, string? shop, string? cuisine)
    {
        if (amenity == "marketplace") return "Feira e mercado";
        if (amenity == "food_court") return "Praça de alimentação";
        var food = NameNormalizer.RemoveDiacritics(cuisine ?? "").ToLowerInvariant().Split([';', ','], StringSplitOptions.TrimEntries);
        bool Named(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(NameNormalizer.RemoveDiacritics(name).ToLowerInvariant(),
            pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (food.Contains("acai") || Named(@"\b(acai|acaiteria|acailandia)\b")) return "Açaí";
        if (food.Contains("pizza") || Named(@"\b(pizza|pizzaria)\b")) return "Pizzaria";
        if (food.Contains("tacaca") || Named(@"\btacaca\b")) return "Tacacá";
        if (food.Contains("burger") || Named(@"\bhamburgueria\b")) return "Hamburgueria";
        return shop switch
        {
            "bakery" => "Padaria", "pastry" or "confectionery" or "chocolate" => "Confeitaria",
            "greengrocer" => "Hortifruti", "seafood" => "Peixaria", "deli" or "cheese" or "food" => "Empório",
            "ice_cream" => "Sorveteria", "coffee" or "tea" => "Cafeteria",
            _ => amenity switch { "cafe" => "Cafeteria", "fast_food" => "Lanchonete", "bar" or "pub" => "Bar", "ice_cream" => "Sorveteria", _ => "Restaurante" }
        };
    }
}
