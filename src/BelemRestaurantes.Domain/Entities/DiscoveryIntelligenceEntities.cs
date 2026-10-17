using System;

namespace BelemRestaurantes.Domain.Entities;

public enum DiscoveryTaskType
{
    NewDiscovery = 1,
    UpdateExisting = 2,
    Enrichment = 3,
    SourceVerification = 4,
    Deduplication = 5,
    ClosureValidation = 6,
    MediaReview = 7,
    ConflictResolution = 8
}

public enum DiscoveryIntelligenceJobStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    DeadLetter = 5
}

public enum RestaurantBusinessStatus
{
    Operational = 1,
    TemporarilyClosed = 2,
    PermanentlyClosed = 3
}

public sealed class GeoDistrictLocality
{
    public int Id { get; set; }
    public uint MunicipalityIbge { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string LocalityType { get; set; } = "locality"; // district, island, locality, tourist_zone
    public bool IsTouristHighlight { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DiscoveryIntelligenceJob
{
    public long Id { get; set; }
    public string TaskType { get; set; } = "new_discovery";
    public long? RestaurantId { get; set; }
    public string SourceProvider { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public string Status { get; set; } = "pending";
    public string? PayloadJson { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 4;
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CommercialInvoice
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public long? ClaimId { get; set; }
    public string PlanCode { get; set; } = "destaque";
    public uint AmountCents { get; set; }
    public string PixTxid { get; set; } = string.Empty;
    public string PixCopyPaste { get; set; } = string.Empty;
    public string PixStatus { get; set; } = "pending"; // pending, paid, expired, cancelled
    public string AuthHash { get; set; } = string.Empty;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
