using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public interface IGeoMunicipalityRepository
{
    Task<IReadOnlyList<GeoMunicipality>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<MunicipalityCoverageStats>> GetCoverageStatsAsync(CancellationToken ct);
    Task<GeoMunicipality?> GetByIbgeCodeAsync(uint ibgeCode, CancellationToken ct);
    Task<GeoMunicipality?> GetByNameAsync(string name, CancellationToken ct);
    Task UpdateScanTimestampAsync(uint ibgeCode, DateTimeOffset scannedAt, string status, CancellationToken ct);
    Task<IReadOnlyList<GeoMunicipality>> GetPriorityUnscannedAsync(int limit, CancellationToken ct);
}

public interface IEnrichmentQueueRepository
{
    Task<long> EnqueueAsync(long restaurantId, int priority, string fields, CancellationToken ct);
    Task<IReadOnlyList<RestaurantEnrichmentQueueItem>> ClaimDueEnrichmentsAsync(int batchSize, string instanceId, TimeSpan lockDuration, CancellationToken ct);
    Task CompleteEnrichmentAsync(long queueId, CancellationToken ct);
    Task FailEnrichmentAsync(long queueId, string error, TimeSpan backoff, CancellationToken ct);
    Task<int> CountPendingAsync(CancellationToken ct);
}

public interface IRestaurantAttributeHistoryRepository
{
    Task RecordHistoryAsync(RestaurantAttributeHistory history, CancellationToken ct);
    Task<IReadOnlyList<RestaurantAttributeHistory>> GetHistoryAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantAttributeHistory>> ListRecentChangesAsync(int limit, CancellationToken ct);
}

public interface IRestaurantMediaRepository
{
    Task<long> AddMediaAsync(RestaurantMedia media, CancellationToken ct);
    Task<IReadOnlyList<RestaurantMedia>> GetMediaByRestaurantAsync(long restaurantId, bool onlyApproved = true, CancellationToken ct = default);
    Task<bool> ApproveMediaAsync(long mediaId, CancellationToken ct);
    Task<bool> RejectMediaAsync(long mediaId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantMedia>> ListPendingModerationAsync(int limit, CancellationToken ct);
}

public interface IEnrichmentService
{
    /// <summary>
    /// Processa o enriquecimento de um restaurante:
    /// - Coleta dados complementares da fonte ou IA
    /// - Normaliza e valida com Cuia IA (Gemini/Groq/DeepSeek)
    /// - Respeita prioridade do proprietário verificado (Tier 1)
    /// - Registra histórico de atributos e gera pendência de moderação se houver divergência
    /// </summary>
    Task<bool> ProcessRestaurantEnrichmentAsync(long restaurantId, string fields, CancellationToken ct);

    /// <summary>
    /// Executa um ciclo de processamento da fila de enriquecimento.
    /// </summary>
    Task<int> RunEnrichmentCycleAsync(int batchSize, CancellationToken ct);

    /// <summary>Enfileira publicados sem foto para busca no Wikimedia Commons.</summary>
    Task<int> EnqueueLicensedPhotoSweepAsync(int limit, CancellationToken ct);
}
