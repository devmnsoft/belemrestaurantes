using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Application.Services;

/// <summary>
/// Roteador da Cuia IA. Escolhe provedor pela ordem configurada, cai para o
/// seguinte em falha, respeita orçamento e valida rigidamente a saída.
/// </summary>
/// <remarks>
/// O provedor preferido é o primeiro da cadeia que esteja configurado e com
/// orçamento disponível. A cadeia nunca inclui <see cref="AiProvider.Disabled"/>.
/// </remarks>
public sealed class AiRouter(
    IEnumerable<IAiProvider> providers,
    ICostLedger costLedger,
    IOptionsSnapshotLike options,
    ILogger<AiRouter> logger) : IAiRouter
{
    private readonly IAiProvider[] _providers = [.. providers];
    private readonly Dictionary<AiProvider, Health> _health = [];

    private sealed record Health(int Success, int Failure, long LastCostMicrosUsd, DateTimeOffset? LastUsedAt, bool CircuitOpen, DateTimeOffset? OpenUntil);

    public IReadOnlyList<AiProvider> ResolveChain()
    {
        var chain = new List<AiProvider>();
        foreach (var candidate in options.ProviderChain)
        {
            if (candidate == AiProvider.Disabled)
            {
                continue;
            }

            var provider = _providers.FirstOrDefault(p => p.Provider == candidate);
            if (provider is { IsConfigured: true })
            {
                chain.Add(candidate);
            }
        }

        return chain;
    }

    public IReadOnlyList<ProviderHealthReport> GetHealth() =>
        _providers.Select(p =>
        {
            var h = GetHealthOf(p.Provider);
            return new ProviderHealthReport(
                p.Provider,
                p.IsConfigured,
                h.Success,
                h.Failure,
                h.LastCostMicrosUsd > 0 ? h.LastCostMicrosUsd / 1_000_000m : null,
                h.LastUsedAt,
                h.CircuitOpen);
        }).ToArray();

    public async Task<AiClassification?> ClassifyAsync(string operation, string payload, CancellationToken ct)
    {
        var chain = ResolveChain();
        if (chain.Count == 0)
        {
            logger.LogDebug("Nenhum provedor de IA configurado; tarefa '{Operation}' ignorada.", operation);
            return null;
        }

        var request = AiCompletionRequest.ForClassification(operation, payload);
        var usedFallback = false;

        foreach (var provider in chain)
        {
            var health = GetHealthOf(provider);
            if (health.CircuitOpen)
            {
                logger.LogWarning("Circuito aberto para {Provider}; pulando nesta chamada.", provider);
                continue;
            }

            if (!await costLedger.HasBudgetRemaining(
                    provider.ToString(),
                    await costLedger.MonthToDateUsdAsync(provider.ToString(), DateTimeOffset.UtcNow, ct),
                    ct))
            {
                logger.LogWarning("Orçamento do provedor {Provider} esgotado no mês.", provider);
                continue;
            }

            try
            {
                var response = await ExecuteAsync(provider, request, ct);

                if (response is null || !response.IsValid)
                {
                    RegisterFailure(provider, "saída inválida ou vazia");
                    usedFallback = true;
                    continue;
                }

                var parsed = AiResponseValidator.TryParseClassification(
                    response.RawContent, provider, usedFallback, out var classification);

                if (parsed is null)
                {
                    RegisterFailure(provider, "JSON fora do schema");
                    usedFallback = true;
                    continue;
                }

                RegisterSuccess(provider, response.CostMicrosUsd);

                await costLedger.RecordAsync(
                    new AiCostEntry(
                        0, provider.ToString(), operation,
                        response.InputTokens, response.OutputTokens,
                        response.CostMicrosUsd, true, DateTimeOffset.UtcNow),
                    ct);

                return classification;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Provedor {Provider} falhou em '{Operation}'; tentando fallback.", provider, operation);
                RegisterFailure(provider, ex.Message);
                usedFallback = true;

                await costLedger.RecordAsync(
                    new AiCostEntry(0, provider.ToString(), operation, 0, 0, 0, false, DateTimeOffset.UtcNow, ex.Message),
                    ct);
            }
        }

        logger.LogWarning("Nenhum provedor respondeu à tarefa '{Operation}'.", operation);
        return null;
    }

    private async Task<AiCompletionResponse?> ExecuteAsync(AiProvider provider, AiCompletionRequest request, CancellationToken ct)
    {
        var impl = _providers.First(p => p.Provider == provider);
        var response = await impl.CompleteAsync(request, ct);

        // Custo é sempre recomputado aqui, a partir do preço do provedor: não
        // confiamos no valor que o serviço externo possa reportar.
        var cost = impl.Price.EstimateCost(response.InputTokens, response.OutputTokens);
        return response with { CostMicrosUsd = cost };
    }

    private Health GetHealthOf(AiProvider provider) =>
        _health.TryGetValue(provider, out var h) ? h : new Health(0, 0, 0, null, false, null);

    private void RegisterSuccess(AiProvider provider, long costMicrosUsd)
    {
        var h = GetHealthOf(provider);
        _health[provider] = h with
        {
            Success = h.Success + 1,
            Failure = 0,
            LastCostMicrosUsd = costMicrosUsd,
            LastUsedAt = DateTimeOffset.UtcNow,
            CircuitOpen = false,
            OpenUntil = null,
        };
    }

    private void RegisterFailure(AiProvider provider, string reason)
    {
        var h = GetHealthOf(provider);
        var failures = h.Failure + 1;

        // Aberta após 5 falhas seguidas; fecha sozinha em 2 minutos.
        var shouldOpen = failures >= CircuitFailureThreshold;
        _health[provider] = h with
        {
            Failure = failures,
            Success = 0,
            CircuitOpen = shouldOpen,
            OpenUntil = shouldOpen ? DateTimeOffset.UtcNow.Add(CircuitBreakDuration) : null,
        };

        logger.LogWarning("Provedor {Provider} falhou ({Reason}). Total seguido: {Count}.", provider, reason, failures);
    }

    public const int CircuitFailureThreshold = 5;
    public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromMinutes(2);
}

/// <summary>Opções injetadas, isoladas para permitir fakes em teste.</summary>
public interface IOptionsSnapshotLike
{
    IReadOnlyList<AiProvider> ProviderChain { get; }
}

/// <summary>
/// Valida a saída de IA contra o schema. Texto que não é JSON válido é
/// descartado — nunca é interpretado parcialmente nem "corrigido" por heurística.
/// </summary>
public static class AiResponseValidator
{
    public static AiClassification? TryParseClassification(
        string? rawContent,
        AiProvider provider,
        bool usedFallback,
        out AiClassification? classification)
    {
        classification = null;
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        // Remove cercas de markdown, caso o modelo as tenha incluído.
        var trimmed = rawContent.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var category = ReadString(root, "categoria") ?? ReadString(root, "category");
        var neighborhood = ReadString(root, "bairro") ?? ReadString(root, "neighborhood");
        var city = ReadString(root, "cidade") ?? ReadString(root, "city");
        var confidence = ReadConfidence(root);

        // Todo campo passa pelas mesmas regras do resto do sistema.
        classification = new AiClassification(
            CategoryRules.Clean(category),
            NeighborhoodRules.Clean(neighborhood),
            string.IsNullOrWhiteSpace(city) ? null : city.Trim(),
            confidence,
            provider,
            usedFallback);

        return classification;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static double ReadConfidence(JsonElement root)
    {
        // O prompt pede "confianca" (sem cedilha, para manter o JSON em ASCII);
        // "confidence" é aceito como alternativa de modelos em inglês.
        if (!root.TryGetProperty("confianca", out var element) &&
            !root.TryGetProperty("confidence", out element))
        {
            return 0d;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            // Modelos às vezes devolvem 0-100 em vez de 0-1.
            if (number > 1d && number <= 100d)
            {
                number /= 100d;
            }

            return Math.Clamp(number, 0d, 1d);
        }

        return 0d;
    }
}