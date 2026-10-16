namespace BelemRestaurantes.Domain.Entities;

public sealed class ExperiencePhoto
{
    public long Id { get; set; }
    public long ExperienceId { get; set; }
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ThumbnailName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/webp";
    public int FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Caption { get; set; }
    public string PhotoType { get; set; } = "community"; // community, official, illustrative
    public string ModerationStatus { get; set; } = "pending"; // pending, approved, rejected
    public string? ModeratedBy { get; set; }
    public DateTimeOffset? ModeratedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
