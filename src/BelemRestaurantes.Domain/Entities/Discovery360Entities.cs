using System;
using BelemRestaurantes.Domain.Enums;

namespace BelemRestaurantes.Domain.Entities;

public enum MunicipalityCoverageStatus
{
    Unscanned = 0,
    InProgress = 1,
    Covered = 2,
    Stale = 3
}

public enum EnrichmentState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum MediaPriorityTier
{
    VerifiedOwner = 1,
    CommunityApproved = 2,
    LicensedExternal = 3,
    Illustration = 4
}

public enum MediaType
{
    PhotoDish = 1,
    PhotoAmbiance = 2,
    PhotoFacade = 3,
    VideoEmbed = 4
}

public sealed class GeoMunicipality
{
    public int Id { get; set; }
    public uint IbgeCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Mesoregion { get; set; } = string.Empty;
    public string Microregion { get; set; } = string.Empty;
    public bool IsTouristPole { get; set; }
    public int PriorityLevel { get; set; } = 1;
    public DateTimeOffset? LastScannedAt { get; set; }
    public string CoverageStatus { get; set; } = "unscanned";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MunicipalityCoverageStats
{
    public int IbgeCode { get; set; }
    public string MunicipalityName { get; set; } = string.Empty;
    public string Mesoregion { get; set; } = string.Empty;
    public bool IsTouristPole { get; set; }
    public string CoverageStatus { get; set; } = "unscanned";
    public DateTimeOffset? LastScannedAt { get; set; }
    public int TotalDiscovered { get; set; }
    public int TotalApproved { get; set; }
    public int TotalPending { get; set; }
    public int TotalWithoutPhotos { get; set; }
    public int TotalStale { get; set; }
}

public sealed class RestaurantEnrichmentQueueItem
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public int Priority { get; set; } = 1;
    public string State { get; set; } = "pending";
    public string FieldsToEnrich { get; set; } = "all";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class RestaurantAttributeHistory
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = string.Empty;
    public string SourceType { get; set; } = "crawler_schema";
    public string? SourceReference { get; set; }
    public decimal ConfidenceScore { get; set; } = 0.800m;
    public bool RequiresModeration { get; set; }
    public bool IsApplied { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RestaurantMedia
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string MediaType { get; set; } = "photo_dish";
    public string MediaUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? EmbedHtml { get; set; }
    public int PriorityTier { get; set; } = 3;
    public string SourceOrigin { get; set; } = string.Empty;
    public string AttributionText { get; set; } = string.Empty;
    public string LicenseType { get; set; } = "with_permission";
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
