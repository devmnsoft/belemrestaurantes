using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public interface IGeoDistrictLocalityRepository
{
    Task<IReadOnlyList<GeoDistrictLocality>> ListByMunicipalityAsync(uint municipalityIbge, CancellationToken ct);
    Task<IReadOnlyList<GeoDistrictLocality>> ListTouristHighlightsAsync(CancellationToken ct);
    Task<GeoDistrictLocality?> GetByIdAsync(int id, CancellationToken ct);
    Task<GeoDistrictLocality?> GetByNameAsync(string name, CancellationToken ct);
}

public interface IDiscoveryIntelligenceQueue
{
    Task<long> EnqueueJobAsync(
        DiscoveryTaskType taskType,
        long? restaurantId,
        string sourceProvider,
        int priority,
        string? payloadJson,
        CancellationToken ct);

    Task<IReadOnlyList<DiscoveryIntelligenceJob>> ClaimDueJobsAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct);

    Task CompleteJobAsync(long jobId, CancellationToken ct);
    Task FailJobAsync(long jobId, string error, TimeSpan backoff, CancellationToken ct);
    Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyDictionary<string, int>> GetJobCountsByStatusAsync(CancellationToken ct);
}

public interface ICommercialMonetizationRepository
{
    Task<CommercialInvoice> CreateInvoiceAsync(
        long restaurantId,
        long? claimId,
        string planCode,
        uint amountCents,
        CancellationToken ct);

    Task<CommercialInvoice?> GetByTxidAsync(string pixTxid, CancellationToken ct);
    Task<bool> ConfirmPaymentAsync(string pixTxid, string authHash, DateTimeOffset paidAt, CancellationToken ct);
    Task<IReadOnlyList<CommercialInvoice>> ListInvoicesByRestaurantAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<CommercialInvoice>> ListRecentInvoicesAsync(int limit, CancellationToken ct);
}

public interface IDiscoveryIntelligenceOrchestrator
{
    /// <summary>
    /// Processa o ciclo central do Cuia Discovery Intelligence (a cada 10 min):
    /// - Descoberta
    /// - Atualização
    /// - Enriquecimento
    /// - Verificação de encerramento de atividades (permanently/temporarily closed)
    /// - Identificação de duplicidades
    /// - Revisão de mídias e conflitos
    /// </summary>
    Task<int> RunIntelligenceCycleAsync(int maxJobs, CancellationToken ct);
}
