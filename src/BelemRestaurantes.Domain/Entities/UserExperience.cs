namespace BelemRestaurantes.Domain.Entities;

public sealed class UserExperience
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public int Stars { get; set; } = 5;
    public int ServiceStars { get; set; } = 5;
    public int FlavorStars { get; set; } = 5;
    public int AmbianceStars { get; set; } = 5;
    public int CostBenefitStars { get; set; } = 5;
    public string? ReviewText { get; set; }
    public string? DishesConsumed { get; set; }
    public decimal? PricePaid { get; set; }
    public string Visibility { get; set; } = "public"; // public, private, unlisted
    public string? UnlistedToken { get; set; }
    public string Status { get; set; } = "pending"; // draft, pending, approved, rejected, deleted
    public string? RejectionReason { get; set; }
    public string? AppealText { get; set; }
    public string AppealStatus { get; set; } = "none"; // none, pending, accepted, denied
    public string? AppealReviewedBy { get; set; }
    public DateTimeOffset? AppealReviewedAt { get; set; }
    public int LikesCount { get; set; }
    public int CommentsCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
}
