using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record CreateExperienceInput(
    long RestaurantId,
    string UserId,
    string AuthorName,
    int Stars,
    int ServiceStars,
    int FlavorStars,
    int AmbianceStars,
    int CostBenefitStars,
    string? ReviewText,
    string? DishesConsumed,
    decimal? PricePaid,
    string Visibility // public, private, unlisted
);

public sealed record UpdateExperienceInput(
    long Id,
    string UserId,
    int Stars,
    int ServiceStars,
    int FlavorStars,
    int AmbianceStars,
    int CostBenefitStars,
    string? ReviewText,
    string? DishesConsumed,
    decimal? PricePaid,
    string Visibility
);

public sealed record ExperienceView(
    long Id,
    long RestaurantId,
    string RestaurantName,
    string RestaurantCity,
    string UserId,
    string AuthorName,
    int Stars,
    int ServiceStars,
    int FlavorStars,
    int AmbianceStars,
    int CostBenefitStars,
    string? ReviewText,
    string? DishesConsumed,
    decimal? PricePaid,
    string Visibility,
    string? UnlistedToken,
    string Status,
    string? RejectionReason,
    string AppealStatus,
    string? AppealText,
    int LikesCount,
    int CommentsCount,
    IReadOnlyList<ExperiencePhotoView> Photos,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    bool HasLiked = false
);

public sealed record ExperiencePhotoView(
    long Id,
    long ExperienceId,
    long RestaurantId,
    string FileName,
    string ThumbnailName,
    string? Caption,
    string PhotoType,
    string ModerationStatus,
    int Width,
    int Height
);

public sealed record CommentView(
    long Id,
    long ExperienceId,
    string UserId,
    string AuthorName,
    string CommentText,
    DateTimeOffset CreatedAt
);

public interface IExperienceService
{
    Task<long> CreateAsync(CreateExperienceInput input, CancellationToken ct);
    Task<bool> UpdateAsync(UpdateExperienceInput input, CancellationToken ct);
    Task<bool> DeleteAsync(long id, string userId, bool isAdmin, CancellationToken ct);
    Task<ExperienceView?> GetByIdAsync(long id, string? viewerUserId, string? unlistedToken, CancellationToken ct);
    Task<IReadOnlyList<ExperienceView>> ListPublicByRestaurantAsync(long restaurantId, int page, int pageSize, string? viewerUserId, CancellationToken ct);
    Task<IReadOnlyList<ExperienceView>> ListPublicFeedAsync(int page, int pageSize, string? sort, string? viewerUserId, CancellationToken ct);
    Task<IReadOnlyList<ExperienceView>> ListUserExperiencesAsync(string userId, CancellationToken ct);

    // Moderação e Auditoria
    Task<IReadOnlyList<ExperienceView>> ListPendingModerationAsync(int page, int pageSize, CancellationToken ct);
    Task<bool> ModerateAsync(long id, string newStatus, string? reason, string moderatorActor, CancellationToken ct);
    Task<bool> SubmitAppealAsync(long id, string userId, string appealText, CancellationToken ct);
    Task<bool> ReviewAppealAsync(long id, bool accept, string moderatorActor, CancellationToken ct);

    // Social: Likes, Comentários, Denúncias
    Task<bool> ToggleLikeAsync(long experienceId, string userId, CancellationToken ct);
    Task<bool> HasUserLikedAsync(long experienceId, string userId, CancellationToken ct);
    Task<long> AddCommentAsync(long experienceId, string userId, string authorName, string commentText, CancellationToken ct);
    Task<IReadOnlyList<CommentView>> ListCommentsAsync(long experienceId, CancellationToken ct);
    Task<bool> DeleteCommentAsync(long commentId, string userId, bool isAdmin, CancellationToken ct);
    Task<long> ReportAsync(string reporterUserId, string targetType, long targetId, string reason, string? notes, CancellationToken ct);
    Task<IReadOnlyList<CommunityReport>> ListPendingReportsAsync(int page, int pageSize, CancellationToken ct);
    Task<bool> ReviewReportAsync(long reportId, bool uphold, string moderatorActor, CancellationToken ct);
}
