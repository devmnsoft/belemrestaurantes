using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Fonte de descoberta. A única implementação de produção hoje é o Google Places.</summary>
public interface IPlaceDiscoveryProvider
{
    string Name { get; }

    /// <summary>Indica se há credencial configurada. Sem isso o conector fica "nao configurado".</summary>
    bool IsConfigured { get; }

    Task<PlaceSearchResult> SearchAsync(
        string textQuery,
        string? cityFilter,
        CancellationToken ct);

    /// <summary>
    /// Detalhes de um Place ID específico. Só campos com permissão de exibição
    /// são solicitados e retornados.
    /// </summary>
    Task<DiscoveredPlace?> GetDetailsAsync(string externalId, CancellationToken ct);
}

/// <summary>
/// Camada de IA. A saída é tratada como não confiável: o implementador deve
/// validar o JSON e nunca devolver texto livre para as regras de negócio.
/// </summary>
public interface IAiProvider
{
    AiProvider Provider { get; }

    bool IsConfigured { get; }

    TokenPrice Price { get; }

    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct);
}

/// <summary>
/// Roteador de IA: escolhe provedor, aplica fallback e registra custo.
/// </summary>
public interface IAiRouter
{
    /// <summary>Executa a tarefa no provedor preferido, caindo para os alternativos em falha.</summary>
    Task<AiClassification?> ClassifyAsync(string operation, string payload, CancellationToken ct);

    IReadOnlyList<AiProvider> ResolveChain();

    IReadOnlyList<ProviderHealthReport> GetHealth();
}

public sealed record ProviderHealthReport(
    AiProvider Provider,
    bool Configured,
    int SuccessCount,
    int FailureCount,
    decimal? LastCostMicrosUsd,
    DateTimeOffset? LastUsedAt,
    bool CircuitOpen)
{
    public double SuccessRate => SuccessCount + FailureCount == 0
        ? 0d
        : (double)SuccessCount / (SuccessCount + FailureCount);
}

/// <summary>Contabilidade de custo para o painel de APIs.</summary>
public interface ICostLedger
{
    /// <summary>Free calls still respect the operator pause, without requiring a positive paid budget.</summary>
    Task<bool> CanUseFreeTierAsync(string provider, CancellationToken ct) => Task.FromResult(false);
    Task RecordAsync(AiCostEntry entry, CancellationToken ct);

    Task<decimal> MonthToDateUsdAsync(string provider, DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<(string Provider, decimal CostUsd)>> BreakdownAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Verifica se ainda há orçamento antes de gastar.</summary>
    Task<bool> HasBudgetRemaining(string provider, decimal alreadySpentUsd, CancellationToken ct);

    /// <summary>Uso recente, sem mensagem de erro nem conteúdo da consulta.</summary>
    Task<IReadOnlyList<AiCostActivity>> RecentAsync(int limit, CancellationToken ct);
}

public sealed record AiCostActivity(
    string Provider,
    string Operation,
    bool Success,
    decimal CostUsd,
    DateTimeOffset OccurredAt);

/// <summary>Relógio injetável para tornar tempo testável.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Conector social oficial. Sóassociam perfis mediante autorização.</summary>
public interface ISocialConnector
{
    string Name { get; }

    string Network { get; }

    bool IsConfigured { get; }

    /// <summary>Verifica se a conta do estabelecimento autorizou a vínculo.</summary>
    Task<SocialProfileLink?> ResolveAuthorizedProfileAsync(string externalProfileId, CancellationToken ct);
}

public sealed record SocialProfileLink(
    string Network,
    string ExternalId,
    string ProfileUrl,
    string AuthorizationReference,
    DateTimeOffset AuthorizedAt);
