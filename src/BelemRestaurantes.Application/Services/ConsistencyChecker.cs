using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Services;

/// <summary>
/// Compara a ficha do catálogo com o que foi re-observado na fonte e aponta
/// divergências. Inconsistência vira pendência de moderação; nunca vira
/// publicação automática nem sobrescrita silenciosa.
/// </summary>
public sealed class ConsistencyChecker(IModerationRepository moderation)
{
    /// <summary>
    /// Campos comparados. Quando ambos os lados têm valor e eles diferem,
    /// a diferença é registrada com a severidade indicada.
    /// </summary>
    private static readonly (string Field, bool Critical)[] ComparedFields =
    [
        ("name", false),
        ("neighborhood", false),
        ("category", false),
        ("address", false),
        ("city", true)
    ];

    public async Task<IReadOnlyList<ConsistencyFinding>> CheckAsync(
        long restaurantId,
        RestaurantView current,
        DiscoveredPlace observed,
        CancellationToken ct)
    {
        var findings = new List<ConsistencyFinding>();
        var observedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = observed.Name,
            ["neighborhood"] = observed.Neighborhood,
            ["category"] = observed.Category,
            ["address"] = observed.Address,
            ["city"] = observed.ResolvedCity
        };

        var currentValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = current.Name,
            ["neighborhood"] = current.Neighborhood,
            ["category"] = current.Category,
            ["address"] = current.Address,
            ["city"] = current.City
        };

        foreach (var (field, isCritical) in ComparedFields)
        {
            var before = currentValues[field];
            var after = observedValues[field];

            // Ausência na nova leitura não é divergência: a fonte pode ter omitido.
            if (string.IsNullOrWhiteSpace(after))
            {
                continue;
            }

            if (FieldsAgree(field, before, after))
            {
                continue;
            }

            findings.Add(isCritical
                ? ConsistencyFinding.Critical(restaurantId, field, before, after)
                : ConsistencyFinding.Warning(restaurantId, field, before, after));

            await moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DataConflict,
                $"Divergência em '{field}': catálogo='{before ?? "(vazio)"}' fonte='{after}'.",
                ct);
        }

        return findings;
    }

    /// <summary>
    /// Comparação tolerante: nomes passam pelo normalizador (acento, caixa e
    /// palavra genérica não devem gerar falso positivo).
    /// </summary>
    private static bool FieldsAgree(string field, string? before, string? after)
    {
        if (string.IsNullOrWhiteSpace(before))
        {
            // Sem valor no catálogo: não há conflito, apenas ausência.
            return true;
        }

        if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return field switch
        {
            "name" => string.Equals(
                NameNormalizer.Normalize(before), NameNormalizer.Normalize(after), StringComparison.Ordinal),
            "category" => string.Equals(
                CategoryRules.Clean(before), CategoryRules.Clean(after), StringComparison.Ordinal),
            "neighborhood" => string.Equals(
                NameNormalizer.Normalize(before), NameNormalizer.Normalize(after), StringComparison.Ordinal),
            "address" => AddressRules.Clean(before) == AddressRules.Clean(after),
            "city" => string.Equals(
                NameNormalizer.Normalize(before), NameNormalizer.Normalize(after), StringComparison.Ordinal),
            _ => false
        };
    }
}