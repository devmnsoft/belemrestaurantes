namespace BelemRestaurantes.Domain.Rules;

/// <summary>Resultado de comparar um candidato descoberto com o catálogo existente.</summary>
public enum MatchOutcome
{
    /// <summary>Nenhum registro compatível: precisa ser criado (e ir para moderação).</summary>
    New = 0,

    /// <summary>Mesmo identificador externo na mesma fonte: é o mesmo lugar, apenas re-observado.</summary>
    SameExternalId = 1,

    /// <summary>Nome normalizado e cidade iguais, Place ID ausente: provavelmente duplicata.</summary>
    ProbableDuplicate = 2,

    /// <summary>Nome compatível em município diferente: merece revisão humana.</summary>
    PossibleAmbiguity = 3
}

/// <summary>Resumo da comparação, com o nível de confiança associado.</summary>
public sealed record DeduplicationResult(
    MatchOutcome Outcome,
    long? MatchedRestaurantId,
    double Confidence,
    string Rationale)
{
    /// <summary>
    /// Só um Place ID idêntico, com confiança máxima, permite tratar como o mesmo
    /// registro sem passar por moderação. Qualquer coisa abaixo disso vira item de
    /// revisão — é a regra "não publicar resultados incertos automaticamente".
    /// </summary>
    public bool IsSafeToAutoMerge => Outcome == MatchOutcome.SameExternalId && Confidence >= 0.99;

    public bool RequiresHumanReview =>
        Outcome is MatchOutcome.ProbableDuplicate or MatchOutcome.PossibleAmbiguity
        || (Outcome == MatchOutcome.SameExternalId && !IsSafeToAutoMerge);
}

/// <summary>Registro já existente, na forma necessária para participar da comparação.</summary>
public sealed record DeduplicationCandidate(
    long Id,
    string NormalizedName,
    string City,
    string? ExternalId = null);

/// <summary>
/// Deduplicação determinística. A IA pode sugerir um vínculo, mas a decisão
/// automática de juntar ou criar registro vem daqui, e é testável sem rede.
/// </summary>
public static class DeduplicationService
{
    /// <summary>Acima deste valor os nomes são tratados como o mesmo estabelecimento.</summary>
    public const double SimilarityThreshold = 0.90;

    public static double Similarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1d;
        }

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0d;
        }

        var distance = Levenshtein(left, right);
        var longest = Math.Max(left.Length, right.Length);
        return 1d - ((double)distance / longest);
    }

    public static DeduplicationResult Compare(
        string? externalId,
        string rawName,
        string city,
        IReadOnlyCollection<DeduplicationCandidate> catalog)
    {
        var normalized = NameNormalizer.Normalize(rawName);
        if (normalized.Length == 0)
        {
            return new DeduplicationResult(MatchOutcome.New, null, 0d, "Nome não normalizável.");
        }

        // 1. Identificador externo do mesmo provedor é a chave mais forte que existe.
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            var byExternal = catalog.FirstOrDefault(c =>
                string.Equals(c.ExternalId, externalId, StringComparison.Ordinal));

            if (byExternal is not null)
            {
                return new DeduplicationResult(
                    MatchOutcome.SameExternalId,
                    byExternal.Id,
                    1d,
                    "Identificador externo idêntico ao de um registro já existente.");
            }
        }

        var normalizedCity = NameNormalizer.Normalize(city);

        // 2. Mesma cidade: igualdade exata do nome normalizado.
        foreach (var candidate in catalog.Where(c => SameCity(c.City, normalizedCity)))
        {
            if (string.Equals(candidate.NormalizedName, normalized, StringComparison.Ordinal))
            {
                return new DeduplicationResult(
                    MatchOutcome.ProbableDuplicate,
                    candidate.Id,
                    0.92,
                    "Nome normalizado e cidade idênticos aos de um registro existente.");
            }
        }

        // 3. Mesma cidade: nomes muito parecidos ("Boi Azul" x "Boi Azul II").
        var closest = catalog
            .Where(c => SameCity(c.City, normalizedCity))
            .Select(c => (Candidate: c, Score: Similarity(c.NormalizedName, normalized)))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (closest.Candidate is not null && closest.Score >= SimilarityThreshold)
        {
            return new DeduplicationResult(
                MatchOutcome.ProbableDuplicate,
                closest.Candidate.Id,
                closest.Score,
                $"Similaridade de nome de {closest.Score:P0} dentro do mesmo município.");
        }

        // 4. Mesmo nome normalizado em outro município: pode ser homônimo ou unidade-pirada.
        var crossCity = catalog
            .Where(c => string.Equals(c.NormalizedName, normalized, StringComparison.Ordinal))
            .OrderBy(c => NameNormalizer.Normalize(c.City).Length)
            .FirstOrDefault();

        if (crossCity is not null)
        {
            return new DeduplicationResult(
                MatchOutcome.PossibleAmbiguity,
                crossCity.Id,
                0.55,
                "Mesmo nome normalizado em outro município; exige conferência humana.");
        }

        return new DeduplicationResult(MatchOutcome.New, null, 1d, "Nenhum registro compatível no catálogo.");
    }

    private static bool SameCity(string left, string normalizedRight) =>
        string.Equals(NameNormalizer.Normalize(left), normalizedRight, StringComparison.Ordinal);

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}