using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

/// <summary>Configuração da consulta de edifícios (Overpass API). O teto diário é aplicado no banco, no ciclo.</summary>
public sealed class OverpassOptions
{
    /// <summary>Instâncias públicas em ordem de preferência (failover). A última bem-sucedida é tentada
    /// primeiro nas consultas seguintes; instâncias chegam e vão (o /api/interpreter é o endpoint público
    /// documentado por todas).</summary>
    public IReadOnlyList<string> Endpoints { get; set; } = new[]
    {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.osm.ch/api/interpreter"
    };
    public int TimeoutSeconds { get; set; } = 20;
    /// <summary>Intervalo mínimo entre requisições (política de uso da Overpass: cadência conservadora por cliente).</summary>
    public int MinimumIntervalMilliseconds { get; set; } = 2100;
}

/// <summary>Overpass API do OpenStreetMap: endpoint transacional fixo, User-Agent identificado no cliente
/// seguro e cadência mínima entre requisições (mesmo tratamento de cortesia dado ao /search do Nominatim
/// neste projeto). Com failover entre instâncias públicas: cada requisição tenta no máximo todas uma vez,
/// partindo da última que respondeu. A URL carrega só a consulta escapada.</summary>
public sealed class OverpassBuildingSource(ISafeHttpClient http, OverpassOptions options, ILogger<OverpassBuildingSource>? logger = null) : IOsmBuildingSource
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static DateTimeOffset nextAllowed;
    private static int _lastGood = -1;

    /// <summary>Idade máxima aceita para os dados do espelho. Mais velho que isso (ou idade ilegível) e o
    /// espelho só pode confirmar, não rejeitar: construções com endereço marcado não desaparecem, mas
    /// "ausência" em snapshot antigo não prova ausência hoje.</summary>
    public const int MaxDataAgeDays = 14;

    /// <summary>Construções (qualquer tag building), comestíveis e restaurantes — o último termo pega tudo
    /// que tem número marcado no OSM (inclusive mall/way que abriga o endereço da ficha).
    /// Importante: a consulta precisa ser multilinha — as instâncias públicas rejeitam a união
    /// ( ... ) escrita em uma linha única com erro de parse no OSM3S.</summary>
    public static string BuildQuery(double latitude, double longitude, int radiusMeters)
    {
        var r = Math.Clamp(radiusMeters, 20, 300);
        var lat = latitude.ToString("F6", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("F6", CultureInfo.InvariantCulture);
        return "[out:json][timeout:20];\n"
            + "(\n"
            + $"  nwr[\"building\"](around:{r},{lat},{lon});\n"
            + $"  nwr[\"amenity\"~\"^(restaurant|fast_food|cafe|bar|pub|food_court)$\"](around:{r},{lat},{lon});\n"
            + $"  nwr[\"shop\"=\"restaurant\"](around:{r},{lat},{lon});\n"
            + $"  nwr[\"addr:housenumber\"](around:{r},{lat},{lon});\n"
            + ");\n"
            + "out center tags;";
    }

    public async Task<OverpassLookupResult> BuildingsNearAsync(double latitude, double longitude, int radiusMeters, CancellationToken ct)
    {
        var endpoints = options.Endpoints is null ? [] : options.Endpoints
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.TrimEnd('/'))
            .Distinct()
            .ToList();
        if (endpoints.Count == 0) return new(false, "no_endpoints", [], false);

        var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 30);
        var data = Uri.EscapeDataString(BuildQuery(latitude, longitude, radiusMeters));
        var start = _lastGood >= 0 && _lastGood < endpoints.Count ? _lastGood : 0;
        var failures = new List<string>(endpoints.Count);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds * endpoints.Count + 5));

        for (var i = 0; i < endpoints.Count; i++)
        {
            var index = (start + i) % endpoints.Count;
            var url = $"{endpoints[index]}?data={data}";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { failures.Add($"{index}:url"); continue; }

            await Gate.WaitAsync(deadline.Token);
            try
            {
                var delay = nextAllowed - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, deadline.Token);
                nextAllowed = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(options.MinimumIntervalMilliseconds, 0, 30000));

                SafeDownloadResult page;
                try
                {
                    page = await http.DownloadAsync(url, timeoutSeconds: timeoutSeconds, ct: deadline.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {
                    logger?.LogWarning("Overpass {Endpoint} indisponível: {FailureType}.", index, ex.GetType().Name);
                    failures.Add($"{index}:erro");
                    continue;
                }
                if (!page.Success) { failures.Add($"{index}:http_{page.StatusCode}"); continue; }
                if (page.ContentType is not null && !page.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                { failures.Add($"{index}:tipo"); continue; }
                _lastGood = index;
                var text = page.ContentText ?? string.Empty;
                return new(true, null, ParseElements(text), IsDataStale(text));
            }
            finally { Gate.Release(); }
        }
        var reason = "unavailable(" + string.Join("|", failures) + ")";
        return new(false, reason.Length <= 64 ? reason : reason[..64], [], false);
    }

    /// <summary>Lê <c>osm3s.timestamp_osm_base</c> da resposta e compara com a idade máxima. ISO-8601 é o
    /// formato padrão das instâncias; número é tratado como epoch em segundos. Ausente, ilegível ou
    /// inconsistente → envelhecido (conservador: o espelho só confirma, não rejeita).</summary>
    public static bool IsDataStale(string json, DateTimeOffset? now = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("osm3s", out var osm3s)
                || !osm3s.TryGetProperty("timestamp_osm_base", out var stampRaw)
                || stampRaw.ValueKind != JsonValueKind.String) return true;
            var stampText = stampRaw.GetString() ?? string.Empty;
            var reference = now ?? DateTimeOffset.UtcNow;
            if (DateTimeOffset.TryParse(stampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp))
                return reference - stamp > TimeSpan.FromDays(MaxDataAgeDays);
            if (long.TryParse(stampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochSeconds) && epochSeconds > 1_000_000_000L)
                return reference - DateTimeOffset.FromUnixTimeSeconds(epochSeconds) > TimeSpan.FromDays(MaxDataAgeDays);
            return true;
        }
        catch (JsonException) { return true; }
    }

    /// <summary>Extrai objetos com coordenada definida (nodes por si, way/relation pelo centro) e as tags
    /// <c>addr:street</c>/<c>addr:housenumber</c>. Respostas acima de 20 elementos são truncadas — o avaliador
    /// rejeita ambiguidade de qualquer forma.</summary>
    public static IReadOnlyList<OsmBuildingElement> ParseElements(string json)
    {
        var list = new List<OsmBuildingElement>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return list; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("elements", out var elements)
                || elements.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in elements.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idRaw) || !idRaw.TryGetInt64(out var id)) continue;
                var kind = item.TryGetProperty("type", out var kindRaw) && kindRaw.ValueKind == JsonValueKind.String ? kindRaw.GetString() ?? "element" : "element";
                var latitude = GetDouble(item, "lat") ?? GetDouble(item, "center", "lat");
                var longitude = GetDouble(item, "lon") ?? GetDouble(item, "center", "lon");
                if (latitude is null || longitude is null) continue;
                list.Add(new OsmBuildingElement(id, kind, latitude.Value, longitude.Value, Tag(item, "addr:street"), Tag(item, "addr:housenumber")));
                if (list.Count >= 20) break;
            }
        }
        return list;
    }

    private static double? GetDouble(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out var next)) return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.Number
            && double.TryParse(current.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? Tag(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("tags", out var tags)
            && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(name, out var t)
            && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
}
