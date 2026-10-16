namespace BelemRestaurantes.Domain.Entities;

public sealed class ExperienceComment
{
    public long Id { get; set; }
    public long ExperienceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CommunityReport
{
    public long Id { get; set; }
    public string ReporterUserId { get; set; } = string.Empty;
    public string TargetType { get; set; } = "experience"; // experience, photo, comment
    public long TargetId { get; set; }
    public string Reason { get; set; } = "offensive"; // offensive, harassment, pii, fake, spam, other
    public string? Notes { get; set; }
    public string Status { get; set; } = "pending"; // pending, reviewed_upheld, reviewed_dismissed
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CuiaRanking
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string CategoryKey { get; set; } = string.Empty;
    public int RankPosition { get; set; }
    public double Score { get; set; }
    public int ApprovedReviewsCount { get; set; }
    public string BadgeTitle { get; set; } = string.Empty;
    public string MethodologySummary { get; set; } = string.Empty;
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LgpdRequest
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string RequestType { get; set; } = "export"; // export, deletion, anonymization, rectification
    public string Status { get; set; } = "pending"; // pending, processing, completed, rejected
    public string? PayloadExport { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
