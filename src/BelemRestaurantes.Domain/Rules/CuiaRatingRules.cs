using System.Globalization;

namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Regras e metodologia transparente para o Selo Cuia.
/// Totalmente independente das notas do Google ou outras fontes externas.
/// </summary>
public static class CuiaRatingRules
{
    public const double PriorMean = 4.0;
    public const int PriorWeight = 3;

    public static readonly IReadOnlyDictionary<string, string> CategoryTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["melhores_belem"] = "Melhores Avaliados de Belém",
        ["melhor_acai"] = "Melhor Açaí",
        ["melhor_tacaca"] = "Melhor Tacacá",
        ["melhor_pizzaria"] = "Melhor Pizzaria",
        ["melhor_custo_beneficio"] = "Melhor Custo-Benefício",
        ["destaque_interior"] = "Destaques do Interior do Pará",
        ["novo_talento"] = "Novos Talentos Gastronômicos",
        ["favorito_comunidade"] = "Favoritos da Comunidade"
    };

    /// <summary>
    /// Calcula a média bayesiana ponderada para evitar que restaurantes com 1 avaliação nota 5
    /// fiquem artificialmente acima de restaurantes com centenas de avaliações consistentes.
    /// Fórmula: (v * R + m * C) / (v + m)
    /// </summary>
    public static double CalculateBayesianScore(double averageStars, int approvedReviewsCount)
    {
        if (approvedReviewsCount <= 0) return 0.0;
        var clampedAvg = Math.Clamp(averageStars, 1.0, 5.0);
        var score = (approvedReviewsCount * clampedAvg + PriorWeight * PriorMean) / (approvedReviewsCount + PriorWeight);
        return Math.Round(score, 2);
    }

    /// <summary>
    /// Retorna a síntese transparente da metodologia aplicada no cálculo.
    /// </summary>
    public static string GetMethodologySummary(double rawAverage, int count, double finalScore)
    {
        var inv = CultureInfo.InvariantCulture;
        return $"Nota calculada via média bayesiana ({finalScore.ToString("F2", inv)}/5.00) sobre {count} avaliação(ões) aprovada(s) pela comunidade (média bruta {rawAverage.ToString("F2", inv)}), ponderada com fator de calibração neutro (m={PriorWeight}, C={PriorMean.ToString("F1", inv)}) para evitar distorções estatísticas.";
    }

    /// <summary>
    /// Classifica um restaurante nas categorias do Selo Cuia conforme atributos, cidade e notas detalhadas.
    /// </summary>
    public static bool MatchesCuiaCategory(string categoryKey, string city, string? restaurantCategory, double costBenefitStars, int favoriteCount, DateTimeOffset createdAt)
    {
        var normCity = NameNormalizer.RemoveDiacritics(city ?? "").ToLowerInvariant();
        var normCat = NameNormalizer.RemoveDiacritics(restaurantCategory ?? "").ToLowerInvariant();

        return categoryKey.ToLowerInvariant() switch
        {
            "melhores_belem" => normCity.Contains("belem"),
            "melhor_acai" => normCat.Contains("acai"),
            "melhor_tacaca" => normCat.Contains("tacaca") || normCat.Contains("paraense") || normCat.Contains("regional"),
            "melhor_pizzaria" => normCat.Contains("pizza"),
            "melhor_custo_beneficio" => costBenefitStars >= 4.0,
            "destaque_interior" => !normCity.Contains("belem") && !normCity.Contains("ananindeua"),
            "novo_talento" => (DateTimeOffset.UtcNow - createdAt).TotalDays <= 180,
            "favorito_comunidade" => favoriteCount >= 1,
            _ => false
        };
    }
}
