using System;
using System.Collections.Generic;

namespace BelemRestaurantes.Domain.Entities;

public sealed class RestaurantClaimRequest
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string RestaurantName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? TaxId { get; set; } // CNPJ ou documento
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? OfficialChannels { get; set; }
    public string VerificationEvidence { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, approved, rejected
    public string? RejectionReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantOwner
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "owner";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantProfile
{
    public long RestaurantId { get; set; }
    public string? OfficialDescription { get; set; }
    public string? Slogan { get; set; }
    public string? History { get; set; }
    public string? CommercialPhone { get; set; }
    public string? CommercialWhatsapp { get; set; }
    public string? CommercialEmail { get; set; }
    public string? DeliveryWhatsapp { get; set; }
    public string? DeliveryIfoodUrl { get; set; }
    public string? DeliveryCustomUrl { get; set; }
    public bool IsAcceptingOrders { get; set; } = true;
    public bool HasWifi { get; set; }
    public bool HasParking { get; set; }
    public bool HasOutdoorSeating { get; set; }
    public bool IsPetFriendly { get; set; }
    public bool IsAccessible { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantSchedule
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public byte DayOfWeek { get; set; } // 0=Domingo a 6=Sábado
    public bool IsClosed { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
}

public sealed class RestaurantSpecialSchedule
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public DateOnly SpecificDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class MenuCategory
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MenuItem> Items { get; set; } = [];
}

public sealed class MenuItem
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public long RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public string? Ingredients { get; set; }
    public string? Allergens { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsRegionalDish { get; set; }
    public string? RegionalDishType { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantAnnouncement
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime? EventDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantReviewReply
{
    public long Id { get; set; }
    public long ExperienceId { get; set; }
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantStat
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public DateOnly StatDate { get; set; }
    public int PageViews { get; set; }
    public int WhatsappClicks { get; set; }
    public int RouteClicks { get; set; }
    public int MenuViews { get; set; }
}

public sealed class RestaurantSocialMedia
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
    public string? EmbedCode { get; set; }
    public bool IsVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CommercialPlan
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public bool BadgeHighlight { get; set; }
    public bool MenuEnabled { get; set; }
    public bool PrioritySupport { get; set; }
    public bool SponsoredExposure { get; set; }
    public bool MultiBranch { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RestaurantSubscription
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string PaymentMethod { get; set; } = "pix";
}

public sealed class MerchantOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long RestaurantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "pending";
    public string PaymentMethod { get; set; } = "pix";
    public string PixCnpj { get; set; } = "18.160.057/0001-13";
    public string? PixCopyPaste { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantCoupon
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public byte? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public DateOnly ValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GastronomicTrail
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CoverImage { get; set; }
    public int EstimatedDurationHours { get; set; } = 4;
    public string? TransportTips { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TrailStop> Stops { get; set; } = [];
    public string? EditorialJson { get; set; }
    public string EditorialState { get; set; } = "legacy";
    public DateTime? ReviewedAt { get; set; }
    public int EditorialVersion { get; set; } = 1;
    public TrailEditorial? Editorial => string.IsNullOrWhiteSpace(EditorialJson) ? null : System.Text.Json.JsonSerializer.Deserialize<TrailEditorial>(EditorialJson);

}

public sealed class TrailStop
{
    public long Id { get; set; }
    public int TrailId { get; set; }
    public long RestaurantId { get; set; }
    public string RestaurantName { get; set; } = string.Empty;
    public string? RestaurantAddress { get; set; }
    public string? RestaurantCategory { get; set; }
    public string? RestaurantPhotoUrl { get; set; }
    public int StopOrder { get; set; } = 1;
    public string? HighlightDish { get; set; }
    public string? Notes { get; set; }
}

public sealed class DiscoveryAlert
{
    public long Id { get; set; }
    public long? RestaurantId { get; set; }
    public string? ExternalId { get; set; }
    public string AlertType { get; set; } = string.Empty; // closed_down, address_changed, schedule_changed, conflict_detected, name_changed
    public string DetailsJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
