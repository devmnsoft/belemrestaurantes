using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Crawler;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>Sprint 58 — geocodificação autônoma: localiza fichas sem coordenada usando o endereço publicado,
/// via Nominatim/OpenStreetMap. Um ciclo não garante novidade aplicada: reserva diária, lease e regras rígidas
/// decidem o que entra no mapa. Fichas com reivindicação aprovada ficam de fora.</summary>
public sealed class GeocodingHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<GeocodingHostedService> logger) : BackgroundService
{
    public static async Task<GeocodingCycle> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var queue = services.GetRequiredService<IGeocodingQueue>();
        var source = services.GetRequiredService<INominatimGeocodingSource>();
        var history = services.GetRequiredService<IRestaurantAttributeHistoryRepository>();
        var options = services.GetRequiredService<GeocodingWorkerOptions>();
        var batchSize = Math.Clamp(options.BatchSize, 1, 20);

        var enqueued = await queue.EnqueueEligibleAsync(batchSize * 2, ct);
        var processed = 0; var placed = 0; var skipped = 0; var deferred = 0;
        while (processed < batchSize)
        {
            var task = await queue.ClaimAsync(ct);
            if (task is null) break;

            // Sprint 65 — cache negativo: o escopo (logradouro, cidade) foi confirmado vazio no OSM dentro da
            // janela de validade. Encerra como no_candidates_street sem consumir unidade do teto diário.
            var streetScope = options.StreetNegativeCacheEnabled ? GeocodingRules.StreetScopeForLookup(task.Address, task.City) : null;
            if (streetScope is not null &&
                await queue.IsStreetRecentlyEmptyAsync(streetScope.StreetKey, streetScope.CityKey, options.StreetNegativeCacheDays, ct))
            {
                await queue.FinishAsync(task.RestaurantId, true, GeocodingRules.NoCandidatesStreet, task.Attempts, null, null, ct);
                processed++;
                skipped++;
                continue;
            }

            processed++;

            var terminal = true;
            var status = "source_error";
            string? appliedPrecision = null; long? appliedPlaceId = null;
            try
            {
                // Sprint 66 — cache de consulta: a mesma consulta (endereço, cidade) respondida de verdade hoje é
                // rejogada sem consumir unidade do teto diário; só respostas com fonte disponível e sem restrição
                // entram no cache — falha/restrrição nunca se congela.
                IReadOnlyList<NominatimCandidate>? pool = options.QueryCacheEnabled
                    ? await queue.TryGetCachedPoolAsync(task.Address, task.City, ct)
                    : null;
                if (pool is null)
                {
                    if (!await queue.TryReserveDailyAsync(options.DailyLimit, ct))
                    {
                        // Sem orçamento para chamadas externas: devolve a ficha sem contar o claim como tentativa
                        // (senão a frente da fila encurralaria fichas em 'failed' sem nunca tê-las consultado) e para o ciclo.
                        await queue.ReleaseClaimAsync(task.RestaurantId, Math.Max(0, task.Attempts - 1), ct);
                        deferred++;
                        break;
                    }
                    var result = await source.SearchAsync(task.Address, task.City, ct);
                    if (!result.SourceAvailable)
                    {
                        await queue.FinishAsync(task.RestaurantId, false, result.FailureReason ?? "source_unavailable", task.Attempts, null, null, ct);
                        deferred++;
                        continue;
                    }
                    if (result.Restricted)
                    {
                        await queue.FinishAsync(task.RestaurantId, true, "robots_restricted", task.Attempts, null, null, ct);
                        skipped++;
                        continue;
                    }
                    pool = result.Candidates;
                    if (options.QueryCacheEnabled)
                        await queue.StoreCachedPoolAsync(task.Address, task.City, pool, ct);
                }
                // Sprint 64 — segundo olhar: o endereço completo não devolveu candidato. Repete a busca uma
                // vez só com o logradouro (mesma avaliação estrita; consome mais uma unidade do mesmo teto
                // diário compartilhado; sem orçamento, adia com backoff como qualquer outra chamada).
                // Sprint 65 — a busca de nível de logradouro que zerar candidatos confirma o cache negativo
                // do escopo (logradouro, cidade) pela janela configurada.
                var streetConfirmedEmpty = false;
                if (pool.Count == 0)
                {
                    var street = GeocodingRules.StreetForSecondLook(task.Address);
                    if (street is null)
                    {
                        streetConfirmedEmpty = true; // a própria consulta já foi feita só com o logradouro
                    }
                    else if (options.StreetSecondLookEnabled)
                    {
                        // Sprint 66 — o segundo olhar também rejoga: logradouro idêntico respondido hoje não consome cota.
                        IReadOnlyList<NominatimCandidate>? streetPool = options.QueryCacheEnabled
                            ? await queue.TryGetCachedPoolAsync(street, task.City, ct)
                            : null;
                        if (streetPool is null)
                        {
                            if (!await queue.TryReserveDailyAsync(options.DailyLimit, ct))
                            { terminal = false; status = "daily_limit"; }
                            else
                            {
                                var streetResult = await source.SearchAsync(street, task.City, ct);
                                if (!streetResult.SourceAvailable) { terminal = false; status = streetResult.FailureReason ?? "source_unavailable"; }
                                else if (streetResult.Restricted) { status = "robots_restricted"; }
                                else
                                {
                                    streetPool = streetResult.Candidates;
                                    if (options.QueryCacheEnabled)
                                        await queue.StoreCachedPoolAsync(street, task.City, streetPool, ct);
                                }
                            }
                        }
                        if (streetPool is not null)
                        {
                            pool = streetPool;
                            streetConfirmedEmpty = pool.Count == 0;
                        }
                    }
                }
                if (terminal && streetConfirmedEmpty && streetScope is not null)
                    await queue.RecordStreetEmptyAsync(streetScope.StreetKey, streetScope.CityKey, ct);
                var decision = GeocodingRules.Evaluate(task.Address, task.City, pool);
                if (!decision.Accepted && decision.Reason == GeocodingRules.NoCandidates && streetConfirmedEmpty)
                    decision = new(false, GeocodingRules.NoCandidatesStreet, null, null, null, null);
                var viaOverpass = false;
                // Sprint 62 — OSM conhece o logradouro mas não tem objeto de endereço/edifício: procura
                // construções/restaurantes ao redor do ponto do logradouro. A consulta consome mais uma
                // unidade do mesmo teto diário compartilhado; sem orçamento, adia com backoff.
                if (!decision.Accepted && decision.Reason == GeocodingRules.UnsupportedType && options.OverpassEnabled)
                {
                    var anchor = GeocodingRules.FindStreetAnchor(task.Address, pool);
                    if (anchor is not null)
                    {
                        if (!await queue.TryReserveDailyAsync(options.DailyLimit, ct))
                        { terminal = false; status = "daily_limit"; }
                        else
                        {
                            var overpass = services.GetRequiredService<IOsmBuildingSource>();
                            var lookup = await overpass.BuildingsNearAsync(anchor.Latitude, anchor.Longitude, options.OverpassRadiusMeters, ct);
                            if (!lookup.SourceAvailable) { terminal = false; status = lookup.FailureReason ?? "overpass_unavailable"; }
                            else
                            {
                                var buildingDecision = GeocodingRules.EvaluateBuildings(task.Address, task.City, lookup.Elements);
                                if (buildingDecision.Accepted)
                                {
                                    // Confirmação vale de qualquer espelho: edifício com endereço marcado não muda de lugar.
                                    decision = buildingDecision;
                                    viaOverpass = true;
                                }
                                else if (!lookup.DataStale)
                                {
                                    // Espelho com dados frescos: a ausência é prova válida — rejeição vira motivo estável.
                                    decision = buildingDecision;
                                }
                                else
                                {
                                    // Espelho envelhecido: falta de objeto no snapshot antigo não prova falta hoje — adia para novo ciclo.
                                    terminal = false;
                                    status = "overpass_stale_data";
                                }
                            }
                        }
                    }
                }
                if (!decision.Accepted)
                {
                    if (terminal) status = decision.Reason; // adiamentos já deixaram o motivo acima
                }
                else if (await queue.ApplyCoordinatesAsync(task.RestaurantId, decision.Latitude!.Value, decision.Longitude!.Value, decision.Precision!, decision.PlaceId!.Value,
                    viaOverpass ? $"OpenStreetMap via Overpass (objeto {decision.PlaceId})" : null, ct))
                {
                    placed++;
                    status = decision.Precision!;
                    appliedPrecision = decision.Precision;
                    appliedPlaceId = decision.PlaceId;
                    await history.RecordHistoryAsync(new RestaurantAttributeHistory
                    {
                        RestaurantId = task.RestaurantId,
                        AttributeName = "coordinate",
                        OldValue = null,
                        NewValue = $"{decision.Latitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)};{decision.Longitude.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}",
                        SourceType = viaOverpass ? "overpass_osm" : "nominatim_osm",
                        SourceReference = viaOverpass ? $"OpenStreetMap objeto {decision.PlaceId} (via Overpass)" : $"OpenStreetMap objeto {decision.PlaceId}",
                        ConfidenceScore = decision.Precision == "rooftop" ? 0.800m : 0.550m,
                        RequiresModeration = false,
                        IsApplied = true
                    }, ct);
                }
                else status = "already_placed";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException)
            {
                terminal = false;
                status = ex is OperationCanceledException ? "timeout" : "source_error";
            }

            await queue.FinishAsync(task.RestaurantId, terminal, status, task.Attempts, appliedPrecision, appliedPlaceId, ct);
            if (!terminal) deferred++;
            else if (status is not ("rooftop" or "approximate")) skipped++;
        }
        return new(enqueued, processed, placed, skipped, deferred);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (configuration.GetValue<bool>("Worker:Geocoding:Enabled"))
                {
                    using var scope = scopes.CreateScope();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Worker:Geocoding:IntervalMinutes", 10), 1, 120)));
                    var result = await RunAsync(scope.ServiceProvider, deadline.Token);
                    logger.LogInformation("Geocodificação: {Enqueued} enfileiradas, {Processed} processadas, {Placed} coordenadas aplicadas, {Skipped} rejeitadas, {Deferred} adiadas.",
                        result.Enqueued, result.Processed, result.Placed, result.Skipped, result.Deferred);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Ciclo de geocodificação: {Failure}.", ex.GetType().Name); }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Worker:Geocoding:IntervalMinutes", 10), 1, 120)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
