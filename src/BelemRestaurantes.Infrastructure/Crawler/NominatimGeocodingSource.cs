using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

/// <summary>Configuração do ciclo de geocodificação (Worker). O teto diário é aplicado no banco, não aqui.</summary>
public sealed class GeocodingWorkerOptions
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 10;
    public int BatchSize { get; set; } = 10;
    /// <summary>Teto de requisições ao Nominatim por dia, compartilhado entre instâncias no banco.</summary>
    public int DailyLimit { get; set; } = 300;
    /// <summary>Sprint 62 — quando a rejeição é "só logradouro" (unsupported_type), consulta edifícios via
    /// Overpass no ponto do logradouro. Cada consulta consome mais uma unidade do mesmo teto diário.</summary>
    public bool OverpassEnabled { get; set; } = true;
    public int OverpassRadiusMeters { get; set; } = 80;
    /// <summary>Sprint 64 — segundo olhar: quando o endereço completo não devolve candidato nenhum, repete a
    /// busca só com o logradouro (mesma avaliação estrita). Consome mais uma unidade do mesmo teto diário.</summary>
    public bool StreetSecondLookEnabled { get; set; } = true;
    /// <summary>Sprint 65 — cache negativo de logradouro: reutiliza confirmações recentes de "rua sem
    /// candidato no OSM" sem nova consulta (e sem consumir cota).</summary>
    public bool StreetNegativeCacheEnabled { get; set; } = true;
    /// <summary>Sprint 65 — validade em dias da confirmação de logradouro vazio. O OSM melhora com o tempo;
    /// após a janela, a primeira ficha do escopo consulta de novo e atualiza o registro.</summary>
    public int StreetNegativeCacheDays { get; set; } = 7;
    /// <summary>Sprint 66 — cache de consulta: rejoga a resposta real de hoje para a mesma consulta
    /// (endereço, cidade) sem consumir unidade do teto diário.</summary>
    public bool QueryCacheEnabled { get; set; } = true;
}

public sealed class NominatimOptions
{
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
    public int Limit { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 12;
    /// <summary>Intervalo mínimo entre requisições (política de uso do Nominatim: máx. 1/s).</summary>
    public int MinimumIntervalMilliseconds { get; set; } = 1100;
    /// <summary>Quando true, um Disallow do robots.txt bloqueia a chamada. Padrão false: o /search é o endpoint
    /// transacional documentado da API (como Overpass/Places neste projeto, sem portão de robots); a cortesia
    /// é garantida pelo UA identificado, pela cadência (maior entre 1/s e Crawl-delay) e pelo teto diário.</summary>
    public bool StrictRobots { get; set; } = false;
}

/// <summary>Nominatim (OpenStreetMap): endpoint público fixo, User-Agent identificado no cliente seguro e
/// cadência de no máximo uma requisição por segundo (ou o Crawl-delay do robots.txt, se maior). O /search é
/// o endpoint transacional documentado da API — como Overpass e Places neste projeto — então o Disallow de
/// wildcard do robots.txt não o bloqueia salvo em <c>StrictRobots</c>. A URL usa só parâmetros escapados.</summary>
public sealed class NominatimGeocodingSource(ISafeHttpClient http, IRobotsTxtService robots, NominatimOptions options, ILogger<NominatimGeocodingSource>? logger = null) : INominatimGeocodingSource
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static DateTimeOffset nextAllowed;

    public static string BuildQuery(string address, string city)
    {
        var value = $"{(address ?? string.Empty).Trim()}, {(city ?? string.Empty).Trim()}, Pará, Brasil".Trim();
        return value[..Math.Min(value.Length, 200)];
    }

    public async Task<NominatimSearchResult> SearchAsync(string address, string city, CancellationToken ct)
    {
        var baseUrl = options.BaseUrl?.TrimEnd('/') ?? "https://nominatim.openstreetmap.org";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            return new(false, false, "invalid_base_url", []);
        var url = $"{baseUrl}/search?q={Uri.EscapeDataString(BuildQuery(address, city))}&format=jsonv2&limit={Math.Clamp(options.Limit, 1, 5)}&countrycodes=br&addressdetails=1";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 3, 30)));
        try
        {
            var access = await robots.CheckAccessAsync(url, "CuiaCrawler", deadline.Token);
            if (options.StrictRobots && !access.IsAllowed) return new(true, true, "robots_restricted", []);
            var intervalMs = Math.Clamp(Math.Max(options.MinimumIntervalMilliseconds, access.CrawlDelaySeconds * 1000), 1000, 30000);
            await Gate.WaitAsync(deadline.Token);
            try
            {
                var delay = nextAllowed - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, deadline.Token);
                nextAllowed = DateTimeOffset.UtcNow.AddMilliseconds(intervalMs);
            }
            finally { Gate.Release(); }

            var page = await http.DownloadAsync(url, timeoutSeconds: Math.Clamp(options.TimeoutSeconds, 3, 30), ct: deadline.Token);
            if (!page.Success) return new(false, false, $"http_{page.StatusCode}", []);
            if (page.ContentType is not null && !page.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return new(false, false, "unexpected_content_type", []);
            return new(true, false, null, ParseCandidates(page.ContentText ?? string.Empty));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            logger?.LogWarning("Nominatim indisponível: {FailureType}.", ex.GetType().Name);
            return new(false, false, "source_unavailable", []);
        }
    }

    public static IReadOnlyList<NominatimCandidate> ParseCandidates(string json)
    {
        var list = new List<NominatimCandidate>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return list; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!TryParseCoordinate(GetProperty(item, "lat"), out var latitude) || !TryParseCoordinate(GetProperty(item, "lon"), out var longitude)) continue;
                var type = GetProperty(item, "type") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(type)) continue;
                var placeId = item.TryGetProperty("place_id", out var pid) && pid.TryGetInt64(out var id) ? id : 0L;
                var address = item.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Object ? addr : default;
                list.Add(new(latitude, longitude, type, placeId,
                    AddressField(address, "road") ?? AddressField(address, "street"),
                    AddressField(address, "house_number") ?? AddressField(address, "house"),
                    AddressField(address, "city") ?? AddressField(address, "town") ?? AddressField(address, "village"),
                    AddressField(address, "state")));
                if (list.Count >= 5) break;
            }
        }
        return list;
    }

    private static string? GetProperty(JsonElement item, string name) =>
        item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? AddressField(JsonElement address, string name) =>
        address.ValueKind == JsonValueKind.Object && address.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool TryParseCoordinate(string? raw, out double value)
    {
        value = 0;
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
