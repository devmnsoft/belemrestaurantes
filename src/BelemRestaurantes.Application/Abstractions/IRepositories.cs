using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Leitura pública do catálogo. Só devolve itens publicados.</summary>
public interface IRestaurantReadRepository
{
    Task<PagedResult<RestaurantView>> SearchAsync(RestaurantSearchQuery query, CancellationToken ct);

    Task<RestaurantView?> GetPublishedAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<string>> ListCitiesAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> ListNeighborhoodsAsync(string city, CancellationToken ct);

    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<string,int>> CountPublishedByCityAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string,int>>(new Dictionary<string,int>());

    Task<IReadOnlyList<CatalogMapPoint>> ListPublishedMapAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CatalogMapPoint>>([]);

    Task<MapPointPage> SearchMapAsync(MapQuery query, CancellationToken ct)
        => Task.FromResult(new MapPointPage([], null));
    Task<MapSummary> GetMapSummaryAsync(CancellationToken ct)
        => Task.FromResult(new MapSummary(0, 0, []));

    Task<CatalogFactSheet?> GetFactSheetAsync(long id, CancellationToken ct)
        => Task.FromResult<CatalogFactSheet?>(null);
}

/// <summary>Operações de ingested do catálogo usadas pelo worker.</summary>
public interface IRestaurantIngestionRepository
{
    async Task<(long Id, bool Created)> UpsertWithOutcomeAsync(DiscoveredPlace place, string provider, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await FindDuplicationCandidatesAsync(place.NormalizedName, place.ResolvedCity, place.ExternalId, ct);
        var id = await UpsertDiscoveredAsync(place, provider, now, ct);
        return (id, !candidates.Any(c => c.Id == id));
    }

    /// <summary>
    /// Busca candidatos para deduplicação numa janela de nomes/cidade.
    /// <paramref name="externalId"/> habilita a comparação por Place ID, que é
    /// o sinal mais forte de que se trata do mesmo estabelecimento.
    /// </summary>
    Task<IReadOnlyList<DeduplicationCandidate>> FindDuplicationCandidatesAsync(
        string normalizedName,
        string city,
        string? externalId,
        CancellationToken ct);

    /// <summary>
    /// Cria o registro em <c>pending</c> ou atualiza o existente. Nunca publica.
    /// Deve gravar também a observação de origem na mesma transação.
    /// </summary>
    Task<long> UpsertDiscoveredAsync(
        DiscoveredPlace place,
        string provider,
        DateTimeOffset now,
        CancellationToken ct);

    Task<DataQuality> RecordObservationAsync(
        long restaurantId,
        string provider,
        string externalId,
        string? sourceUrl,
        string payloadHash,
        DateTimeOffset now,
        CancellationToken ct);

    Task<IReadOnlyList<SourceObservation>> GetObservationsAsync(long restaurantId, CancellationToken ct);
}

/// <summary>Decisão de moderação sobre um estabelecimento.</summary>
public interface IModerationRepository
{
    Task<PagedResult<PendingApproval>> ListPendingAsync(int page, int pageSize, CancellationToken ct,
        string? term = null, string? city = null, string? category = null);

    Task<bool> ApproveAsync(long restaurantId, string actor, CancellationToken ct);

    Task<bool> RejectAsync(long restaurantId, string actor, string reason, CancellationToken ct);

    Task<bool> SuspendAsync(long restaurantId, string actor, string reason, CancellationToken ct);

    Task EnqueueAsync(string itemType, long itemId, ModerationReason reason, string detail, CancellationToken ct);

    Task<int> CountPendingAsync(CancellationToken ct);
}

/// <summary>Fila persistente de descoberta.</summary>
public interface IDiscoveryQueue
{
    Task<int> RetryDeadLettersAsync(string city, CancellationToken ct) => throw new NotSupportedException();
    /// <summary>
    /// Reserva jobs vencidos de forma atômica e sem duplicar entre instâncias.
    /// Deve usar <c>SELECT ... FOR UPDATE SKIP LOCKED</c> (MySQL 8) ou equivalente.
    /// </summary>
    Task<IReadOnlyList<DiscoveryWorkItem>> ClaimDueAsync(
        int batchSize,
        string instanceId,
        TimeSpan lockDuration,
        CancellationToken ct);

    Task CompleteAsync(long jobId, CancellationToken ct);

    Task FailAsync(long jobId, string error, TimeSpan backoff, int maxAttempts, CancellationToken ct);

    /// <summary>Indisponibilidade da fonte: libera a reserva sem incrementar attempts.</summary>
    Task DeferAsync(long jobId, string error, DateTimeOffset retryAt, CancellationToken ct);

    /// <summary>Devolve jobs travados além do prazo (worker caiu) para a fila.</summary>
    Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Cria jobs para todos os alvos vencidos que ainda não têm job aberto.</summary>
    Task<int> EnqueueDueTargetsAsync(DateTimeOffset now, CancellationToken ct);

    Task<int> CountPendingAsync(CancellationToken ct);

    Task<int> CountDeadLetteredAsync(CancellationToken ct);
}

/// <summary>Controle de pausa por cidade ou categoria.</summary>
public interface IDiscoveryTargetRepository
{
    Task<IReadOnlyList<DiscoveryTarget>> ListAsync(CancellationToken ct);

    Task<bool> SetEnabledAsync(long id, bool enabled, string actor, CancellationToken ct);

    Task<bool> SetEnabledForCityAsync(string city, bool enabled, string actor, CancellationToken ct);
}

/// <summary>Registro das execuções de ciclo, para o dashboard.</summary>
public interface ICycleTelemetryRepository
{
    Task RecordTargetAsync(long cycleId, DiscoveryWorkItem work, DiscoveryCycleMetrics metrics, CancellationToken ct)
        => Task.CompletedTask;

    Task<long> StartCycleAsync(string instanceId, DateTimeOffset now, CancellationToken ct);

    Task CompleteCycleAsync(
        long cycleId,
        int claimed,
        int discovered,
        int created,
        int updated,
        int failed,
        int skipped,
        TimeSpan duration,
        CancellationToken ct);
}

/// <summary>Trilha de auditoria. Escrita dentro da transação da ação auditada.</summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string action,
        string entityType,
        long? entityId,
        string? metadataJson,
        CancellationToken ct);
}
