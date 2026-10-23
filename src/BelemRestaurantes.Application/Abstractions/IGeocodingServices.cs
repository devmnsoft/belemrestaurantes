using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Resultado de uma consulta de geocodificação. Falha de rede devolve
/// <see cref="SourceAvailable"/>=false (reprocessa com backoff); bloqueio devolve <see cref="Restricted"/>.</summary>
public sealed record NominatimSearchResult(
    bool SourceAvailable,
    bool Restricted,
    string? FailureReason,
    IReadOnlyList<NominatimCandidate> Candidates);

public sealed record GeocodingTaskRecord(long RestaurantId, string Name, string City, string Address, int Attempts);

/// <summary>Resultado da consulta de edifícios da Overpass. Falha de rede devolve
/// <see cref="SourceAvailable"/>=false (o ciclo adia com backoff; não conta como rejeição).
/// <see cref="DataStale"/> sinaliza espelho com dados antigos (ou idade ilegível): o avaliador pode
/// confirmar uma adoção com ele, mas rejeição dele vira adiamento, não termo final.</summary>
public sealed record OverpassLookupResult(
    bool SourceAvailable,
    string? FailureReason,
    IReadOnlyList<OsmBuildingElement> Elements,
    bool DataStale);

/// <summary>Fonte de edifícios/restaurantes do OpenStreetMap via Overpass API (Sprint 62): endpoint transacional
/// fixo, User-Agent identificado no cliente seguro e cadência mínima entre requisições.</summary>
public interface IOsmBuildingSource
{
    Task<OverpassLookupResult> BuildingsNearAsync(double latitude, double longitude, int radiusMeters, CancellationToken ct);

    /// <summary>Sprint 67 — frescor sticky: <c>true</c> quando todos os espelhos configurados tiveram o dado
    /// mais velho que 14 dias confirmado recentemente, dentro da janela de observação
    /// (<see cref="OverpassOptions.StaleMirrorWindowMinutes"/>). Enquanto isso vale,
    /// <see cref="BuildingsNearAsync"/> responde sem requisição de rede e o ciclo pode adiar a ficha
    /// sem consumir unidade do teto diário.</summary>
    bool AllMirrorsKnownStale { get; }
}

/// <summary>Movimento do ciclo: enfileiradas, processadas, coordenadas aplicadas, rejeitadas e adiadas.</summary>
public sealed record GeocodingCycle(int Enqueued, int Processed, int Placed, int Skipped, int Deferred);

/// <summary>Resumo administrativo da fila. RooftopApplied/ApproximateApplied contam coordenadas geridas por este
/// ciclo e já nas fichas (aplicadas pelo OSM ou ajustadas por revisão), agrupadas pela precisão declarada.
/// <see cref="SkipReasons"/> distribui as linhas terminais rejeitadas pelo último motivo (Sprint 63) para o
/// operador ver sem SQL onde a fila está travando.</summary>
public sealed record GeocodingSummary(
    int Pending,
    int Running,
    int Completed,
    int Failed,
    int Skipped,
    int UsedToday,
    int EligibleNotQueued,
    int PublishedWithoutCoordinates,
    int RooftopApplied,
    int ApproximateApplied,
    IReadOnlyDictionary<string, int> SkipReasons);

/// <summary>Linha da fila para o painel administrativo: estado e motivo último sem esconder nada.
/// Latitude/Longitude refletem o banco agora (para linhas completadas, o par aplicado).</summary>
public sealed record GeocodingTaskRow(
    long RestaurantId,
    string Name,
    string City,
    string State,
    int Attempts,
    string? LastStatus,
    string? ResultPrecision,
    long? OsmPlaceId,
    double? Latitude,
    double? Longitude,
    DateTime UpdatedAt,
    DateTime NextRunAt);

/// <summary>Coordenada removida por revisão humana, com os valores originais para o histórico do atributo.</summary>
public sealed record CoordinateRevertResult(double Latitude, double Longitude, string? Precision, string Source);

/// <summary>Coordenada ajustada por revisão humana, com o par substituído e sua origem para o histórico do atributo.</summary>
public sealed record CoordinateAdjustResult(double OldLatitude, double OldLongitude, string? OldPrecision, string OldSource);

/// <summary>Fonte de geocodificação pública (Nominatim/OpenStreetMap). Endpoint fixo, User-Agent identificado,
/// cadência de no máximo uma requisição por segundo e política de robots.</summary>
public interface INominatimGeocodingSource
{
    Task<NominatimSearchResult> SearchAsync(string address, string city, CancellationToken ct);
}

/// <summary>Fila persistente de geocodificação. Fichas com reivindicação aprovada ou vínculo de dono nunca entram.</summary>
public interface IGeocodingQueue
{
    /// <summary>Enfileira fichas elegíveis sem coordenada e sem tarefa prévia. Retorna quantas entraram.</summary>
    Task<int> EnqueueEligibleAsync(int limit, CancellationToken ct);

    /// <summary>Reserva uma requisição no teto diário compartilhado entre instâncias.
    /// Falso quando o teto de hoje já foi consumido.</summary>
    Task<bool> TryReserveDailyAsync(int dailyLimit, CancellationToken ct);

    Task<GeocodingTaskRecord?> ClaimAsync(CancellationToken ct);

    /// <summary>Grava a coordenada com precisão e procedência, preservando a proteção Tier 1.
    /// <paramref name="sourceOverride"/> altera apenas o texto de origem (ex.: via Overpass na Sprint 62);
    /// por padrão é "OpenStreetMap via Nominatim". Falso quando a ficha já recebeu coordenada por outro meio.</summary>
    Task<bool> ApplyCoordinatesAsync(long restaurantId, double latitude, double longitude, string precision, long placeId, string? sourceOverride = null, CancellationToken ct = default);

    /// <summary>Encerra o item. Termineis não voltam à fila; processáveis ganham backoff exponencial e
    /// desistem após três tentativas.</summary>
    Task FinishAsync(long restaurantId, bool terminal, string status, int attempts, string? precision = null, long? placeId = null, CancellationToken ct = default);

    /// <summary>Leitura administrativa somente.</summary>
    Task<GeocodingSummary> SummaryAsync(CancellationToken ct);

    /// <summary>Pagina a fila para o painel, filtrando por estado e texto no nome da ficha.</summary>
    Task<(IReadOnlyList<GeocodingTaskRow> Items, int Total)> ListTasksAsync(string? state, string? search, int page, int pageSize, CancellationToken ct);

    /// <summary>Devolve um item terminal (skipped/failed) à fila enquanto a ficha segue sem coordenada, com auditoria.
    /// Falso quando o item não está em estado terminal ou a ficha já recebeu coordenada por outro meio.</summary>
    Task<bool> RequeueAsync(long restaurantId, string actor, CancellationToken ct);

    /// <summary>Re-enfileira em lote rejeições e falhas cuja última atualização é anterior ao prazo em dias
    /// (o mapa do OSM melhora com o tempo; o teto diário ainda limita o ritmo). Retorna quantas voltaram.</summary>
    Task<int> RequeueStaleTerminalsAsync(string actor, int olderThanDays, CancellationToken ct);

    /// <summary>Devolve à fila as linhas terminais (skipped/failed) cujo último motivo é exatamente
    /// <paramref name="reason"/> e cuja ficha ainda não tem coordenada — o caso típico é uma mudança de
    /// capacidade (ex.: o fallback Overpass da Sprint 62) que torna rejeições antigas recuperáveis.
    /// Motivo fora do formato <c>[a-z][a-z0-9_]*</c> lança ArgumentException. Retorna quantas voltaram.</summary>
    Task<int> RequeueByReasonAsync(string reason, string actor, CancellationToken ct);

    /// <summary>Sprint 65 — cache negativo: true quando o escopo (logradouro, cidade) foi confirmado vazio
    /// numa busca real de nível de logradouro dentro da janela de validade em dias. Ficha no escopo pode ser
    /// encerrada como no_candidates_street sem consumir cota diária.</summary>
    Task<bool> IsStreetRecentlyEmptyAsync(string streetKey, string cityKey, int validDays, CancellationToken ct);

    /// <summary>Sprint 65 — registra/atualiza a confirmação de que o escopo (logradouro, cidade) está vazio
    /// no OSM. Só deve ser chamado após uma busca real de logradouro ter zerado candidatos.</summary>
    Task RecordStreetEmptyAsync(string streetKey, string cityKey, CancellationToken ct);

    /// <summary>Sprint 65 — devolve à fila, SEM contar contra o orçamento de falhas, uma ficha que foi
    /// reclamada quando o teto diário já não tinha orçamento para chamada externa: restaura o contador de
    /// tentativas ao valor anterior ao claim, limpa o lease e readeia com o mesmo backoff da fórmula padrão.</summary>
    Task ReleaseClaimAsync(long restaurantId, int preClaimAttempts, CancellationToken ct);

    /// <summary>Sprint 66 — cache de consulta: candidatos que uma busca REAL do Nominatim devolveu hoje para
    /// exatamente a mesma consulta (endereço, cidade). Nulo quando não há entrada do dia UTC corrente. Acertos
    /// incrementam o contador de hits da linha.</summary>
    Task<IReadOnlyList<NominatimCandidate>?> TryGetCachedPoolAsync(string address, string city, CancellationToken ct);

    /// <summary>Sprint 66 — grava os candidatos de uma busca real para que consultas idênticas dentro do mesmo
    /// dia UTC sejam rejogadas sem consumir cota diária. Só deve ser chamado após chamada de fato bem-sucedida
    /// (fonte disponível e sem restrição de robots).</summary>
    Task StoreCachedPoolAsync(string address, string city, IReadOnlyList<NominatimCandidate> pool, CancellationToken ct);

    /// <summary>Remove a coordenada aplicada pelo OpenStreetMap/Nominatim (origem reconhecida e sem proteção Tier 1)
    /// e devolve a ficha à fila como rejeição revisada, sem nova tentativa imediata. Retorna os valores removidos
    /// para o histórico; nulo quando não havia coordenada de origem OSM para reverter.</summary>
    Task<CoordinateRevertResult?> RevertAppliedCoordinatesAsync(long restaurantId, string actor, CancellationToken ct);

    /// <summary>Substitui a coordenada aplicada pelo OpenStreetMap/Nominatim por um par conferido por revisão humana.
    /// Só aceita origem reconhecida e sem proteção Tier 1; o novo par deve estar dentro da caixa de contorno do Pará
    /// e com precisão declarada (rooftop/approximate). Fecha a linha como revisada (completed) para o Worker não
    /// sobrescrever; grava auditoria <c>geocoding.adjust</c>. Retorna o par substituído para o histórico; nulo quando
    /// não havia coordenada de origem OSM para ajustar.</summary>
    Task<CoordinateAdjustResult?> AdjustAppliedCoordinatesAsync(long restaurantId, double latitude, double longitude, string precision, string actor, CancellationToken ct);
}
