using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record CuiaAiQuery(
    string Prompt,
    string? City = null,
    string? Occasion = null,
    string? Dietary = null,
    string? Budget = null);

public sealed record CuiaAiRecommendation(
    long Id,
    string Name,
    string City,
    string? Neighborhood,
    string? Category,
    string? Address,
    string? Phone,
    string Reason,
    double? CuiaScore,
    string? PriceRange);

public sealed record CuiaAiRecipeMatch(
    string Slug,
    string Title,
    string Subtitle,
    int TotalMinutes,
    string Difficulty);

public sealed record CuiaAiResponse(
    string AnswerMarkdown,
    IReadOnlyList<CuiaAiRecommendation> Recommendations,
    IReadOnlyList<CuiaAiRecipeMatch> Recipes,
    IReadOnlyList<string> RegionalCuriosities,
    IReadOnlyList<string> SuggestedFollowUps,
    string EngineUsed);

public interface ICuiaAiService
{
    Task<CuiaAiResponse> AskAsync(CuiaAiQuery query, CancellationToken ct);
    Task<string> GetRestaurantAdviceAsync(long restaurantId, CancellationToken ct);
    Task<string> GetRecipeSubstitutionsAsync(string recipeSlug, CancellationToken ct);
}
