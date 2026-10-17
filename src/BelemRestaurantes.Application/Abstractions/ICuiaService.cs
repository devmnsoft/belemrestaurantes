using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record CuiaBadgeView(
    long RestaurantId,
    string RestaurantName,
    string RestaurantCity,
    string CategoryKey,
    string BadgeTitle,
    int RankPosition,
    double Score,
    int ApprovedReviewsCount,
    string MethodologySummary,
    DateTimeOffset CalculatedAt
);

public interface ICuiaService
{
    Task RecalculateRankingsAsync(CancellationToken ct);
    Task<IReadOnlyList<CuiaBadgeView>> GetBadgesForRestaurantAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<CuiaBadgeView>> GetTopRankingsAsync(string categoryKey, int limit, CancellationToken ct);
    Task<IReadOnlyDictionary<string, IReadOnlyList<CuiaBadgeView>>> GetAllHighlightsAsync(CancellationToken ct);
}
