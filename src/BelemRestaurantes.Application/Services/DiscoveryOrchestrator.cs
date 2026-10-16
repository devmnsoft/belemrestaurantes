using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Application.Services;

/// <summary>
/// Orquestra um ciclo de descoberta: consulta a fonte, deduplica, engole no
/// catálogo e registra proveniência. Nenhum caminho daqui publica conteúdo —
/// toda descoberta entra em <c>pending</c> e espera decisão humana.
/// </summary>
public sealed class DiscoveryOrchestrator(
    IPlaceDiscoveryProvider provider,
    IRestaurantIngestionRepository ingestion,
    IModerationRepository moderation,
    IAiRouter aiRouter,
    IClock clock,
    ILogger<DiscoveryOrchestrator> logger)
{
    public async Task<DiscoveryCycleMetrics> RunTargetAsync(DiscoveryWorkItem work, CancellationToken ct)
    {
        var metrics = new MutableMetrics();
        metrics.Claimed++;

        var query = $"{work.Category} em {work.City}, Pará, Brasil";

        PlaceSearchResult search;
        try
        {
            search = await provider.SearchAsync(query, work.City, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Falha de API não pode derrubar o ciclo: apenas este job é marcado como falho.
            metrics.Failed++;
            logger.LogError(
                ex, "Falha ao consultar {Provider} para {City}/{Category}",
                provider.Name, work.City, work.Category);
            return metrics.ToSnapshot();
        }

        foreach (var place in search.Places)
        {
            ct.ThrowIfCancellationRequested();

            var validation = PlaceValidator.Validate(place);
            if (!validation.IsAcceptable)
            {
                metrics.Skipped++;
                logger.LogInformation(
                    "Descartado '{Name}' ({City}): {Reason}", place.Name, place.ResolvedCity, validation.Reason);
                continue;
            }

            try
            {
                await IngestAsync(place, metrics, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Um lugar problemático não invalida os demais do lote.
                metrics.Failed++;
                logger.LogWarning(ex, "Falha ao ingerir o lugar {ExternalId}", place.ExternalId);
            }
        }

        return metrics.ToSnapshot();
    }

    private async Task IngestAsync(DiscoveredPlace place, MutableMetrics metrics, CancellationToken ct)
    {
        // 1. Compara com o catálogo antes de escrever qualquer coisa.
        var candidates = await ingestion.FindDuplicationCandidatesAsync(
            place.NormalizedName, place.ResolvedCity, place.ExternalId, ct);

        var dedup = DeduplicationService.Compare(
            place.ExternalId, place.Name, place.ResolvedCity, candidates);

        // 2. Incerteza pedida: a IA ajuda, mas continua sem publicar.
        AiClassification? suggestion = null;
        if (dedup.RequiresHumanReview)
        {
            suggestion = await aiRouter.ClassifyAsync("dedup-review", BuildReviewPayload(place, dedup), ct);
        }

        var now = clock.UtcNow;
        var restaurantId = await ingestion.UpsertDiscoveredAsync(place, provider.Name, now, ct);

        await ingestion.RecordObservationAsync(
            restaurantId,
            provider.Name,
            place.ExternalId,
            place.GoogleMapsUri,
            ComputePayloadHash(place),
            now,
            ct);

        // 3. Tudo que não tem certeza alta vira pendência explícita de moderação.
        if (suggestion is { CanAutoApply: false })
        {
            await moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.AiLowConfidence,
                $"IA ({suggestion.Provider}) sugeriu categoria '{suggestion.Category}' com confiança {suggestion.Confidence:F2}.",
                ct);
        }

        if (dedup.Outcome is MatchOutcome.ProbableDuplicate or MatchOutcome.PossibleAmbiguity)
        {
            await moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DuplicateSuspected,
                $"Deduplicação: {dedup.Rationale} Confiança {dedup.Confidence:F2}.",
                ct);
        }

        if (dedup.Outcome == MatchOutcome.New)
        {
            metrics.Created++;
        }
        else
        {
            metrics.Updated++;
        }

        metrics.Discovered++;
    }

    private static string BuildReviewPayload(DiscoveredPlace place, DeduplicationResult dedup) =>
        $$"""
        {
          "nome": {{JsonSerializer.Serialize(place.Name)}},
          "cidade": {{JsonSerializer.Serialize(place.ResolvedCity)}},
          "bairro": {{JsonSerializer.Serialize(place.Neighborhood)}},
          "categorias_fonte": {{JsonSerializer.Serialize(place.Types)}},
          "divergencia_deduplicacao": {{JsonSerializer.Serialize(dedup.Rationale)}}
        }
        """;

    private static string ComputePayloadHash(DiscoveredPlace place)
    {
        var material = string.Join(
            '\u001F',
            place.ExternalId,
            place.Name,
            place.Address ?? string.Empty,
            place.Neighborhood ?? string.Empty,
            place.Category ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>Acumulador do ciclo; vira o record imutável devolvido ao worker.</summary>
    private sealed class MutableMetrics
    {
        public int Claimed { get; set; }
        public int Discovered { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }

        public DiscoveryCycleMetrics ToSnapshot() =>
            new(Claimed, Discovered, Created, Updated, Failed, Skipped, TimeSpan.Zero);
    }
}