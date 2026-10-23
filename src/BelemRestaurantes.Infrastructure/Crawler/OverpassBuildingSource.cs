using System;
using System.Collections.Concurrent;
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
    /// <summary>Sprint 67 — janela de validade da observação "este espelho respondeu com dado mais velho que
    /// 14 dias". Dentro da janela o espelho confirmado antigo não recebe requisição (nem consome cadência nem
    /// teto diário); depois dela a sonda se repete uma vez, porque os espelhos se atualizam no seu próprio ritmo.</summary>
    public int StaleMirrorWindowMinutes { get; set; } = 60;
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

    /// <summary>Sprint 67 — idade observada dos dados por espelho, com o mesmo escopo de processo de
    /// <c>_lastGood</c> (instâncias do Worker nunca sobrepõem ciclos, então o estado não precisa ser
    /// compartilhado entre processos; após reinício a sonda se repete, no máximo uma vez).</summary>
    private sealed record MirrorObservation(DateTimeOffset BaseTimestamp, DateTimeOffset ObservedAtUtc);
    private static readonly ConcurrentDictionary<string, MirrorObservation> _mirrorObservations = new();

    /// <summary>Sprint 67 — limpa as observações de processo. Para testes e diagnóstico após deploy.</summary>
    public static void ResetMirrorObservations() => _mirrorObservations.Clear();

    /// <summary>Sprint 67 — registra uma observação da idade dos dados de um espelho (endpoint normalizado,
    /// como o loop interno o trata). Idade ilegível é gravada conservadora pela própria fonte.</summary>
    public static void RecordMirrorObservation(string endpoint, DateTimeOffset baseTimestamp, DateTimeOffset observedAtUtc)
        => _mirrorObservations[endpoint] = new(baseTimestamp, observedAtUtc);

    /// <summary>Sprint 67 — verdadeiro quando todo espelho configurado tem confirmação recente (dentro da janela
    /// de observação) de dado mais velho que 14 dias. Nesse estado a busca custa zero: nenhuma requisição,
    /// nenhuma cadência.</summary>
    public bool AllMirrorsKnownStale
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            var normalized = options.Endpoints is null ? [] : options.Endpoints
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.TrimEnd('/'))
                .Distinct()
                .ToList();
            return normalized.Count > 0 && normalized.All(endpoint => IsMirrorKnownStale(endpoint, now));
        }
    }

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

        var now = DateTimeOffset.UtcNow;
        // Sprint 67 — frescor sticky: tudo confirmado velho dentro da janela responde sem perguntar a ninguém.
        if (endpoints.All(endpoint => IsMirrorKnownStale(endpoint, now)))
            return new(true, null, [], true);

        var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 30);
        var data = Uri.EscapeDataString(BuildQuery(latitude, longitude, radiusMeters));
        var start = _lastGood >= 0 && _lastGood < endpoints.Count ? _lastGood : 0;
        var failures = new List<string>(endpoints.Count);
        var staleSkips = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds * endpoints.Count + 5));

        for (var i = 0; i < endpoints.Count; i++)
        {
            var index = (start + i) % endpoints.Count;
            var url = $"{endpoints[index]}?data={data}";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { failures.Add($"{index}:url"); continue; }

            // Sprint 67 — espelho com dado velho confirmado dentro da janela: pulo sem tocar na rede nem na cadência.
            if (IsMirrorKnownStale(endpoints[index], now)) { staleSkips++; continue; }

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
                // Sprint 67 — toda resposta com idade legível renova a observação (fresca apaga o stale anterior).
                RecordMirrorObservation(endpoints[index],
                    ReadBaseTimestamp(text) ?? DateTimeOffset.UtcNow.AddDays(-(MaxDataAgeDays + 1)),
                    DateTimeOffset.UtcNow);
                return new(true, null, ParseElements(text), IsDataStale(text));
            }
            finally { Gate.Release(); }
        }
        if (staleSkips == endpoints.Count) return new(true, null, [], true);
        var reason = "unavailable(" + string.Join("|", failures) + ")";
        return new(false, reason.Length <= 64 ? reason : reason[..64], [], false);
    }

    /// <summary>Sprint 67 — espelho com observação recente (dentro da janela configurada) de dado mais velho que
    /// 14 dias. Janela zerada ou menor que um minuto desliga o pulo (sempre sonda).</summary>
    private bool IsMirrorKnownStale(string endpoint, DateTimeOffset now)
    {
        if (options.StaleMirrorWindowMinutes < 1) return false;
        var window = TimeSpan.FromMinutes(Math.Clamp(options.StaleMirrorWindowMinutes, 1, 24 * 60));
        return _mirrorObservations.TryGetValue(endpoint, out var observation)
            && now - observation.ObservedAtUtc <= window
            && now - observation.BaseTimestamp > TimeSpan.FromDays(MaxDataAgeDays);
    }

    /// <summary>Lê <c>osm3s.timestamp_osm_base</c> da resposta. ISO-8601 é o formato padrão das instâncias;
    /// número é tratado como epoch em segundos. Ausente ou ilegível → <c>null</c>.</summary>
    public static DateTimeOffset? ReadBaseTimestamp(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("osm3s", out var osm3s)
                || !osm3s.TryGetProperty("timestamp_osm_base", out var stampRaw)
                || stampRaw.ValueKind != JsonValueKind.String) return null;
            var stampText = stampRaw.GetString() ?? string.Empty;
            if (DateTimeOffset.TryParse(stampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp)) return stamp;
            if (long.TryParse(stampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochSeconds) && epochSeconds > 1_000_000_000L)
                return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Compara a idade observada com o teto de 14 dias. Ausente, ilegível ou inconsistente →
    /// envelhecido (conservador: o espelho só confirma, não rejeita).</summary>
    public static bool IsDataStale(string json, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var stamp = ReadBaseTimestamp(json);
        return stamp is null || reference - stamp.Value > TimeSpan.FromDays(MaxDataAgeDays);
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
