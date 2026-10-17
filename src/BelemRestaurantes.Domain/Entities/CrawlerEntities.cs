using System;
using System.Collections.Generic;

namespace BelemRestaurantes.Domain.Entities;

public enum CrawlerSourceType
{
    SitemapXml = 1,
    RssFeed = 2,
    HtmlDirectory = 3,
    StructuredData = 4
}

public enum CrawlerSourceStatus
{
    Active = 1,
    Paused = 2,
    Blocked = 3,
    PendingReview = 4
}

public enum RobotsTxtPermission
{
    Allowed = 1,
    Disallowed = 2,
    Unknown = 3
}

public sealed class CrawlerSource
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string SourceType { get; set; } = "sitemap_xml";
    public string? TargetCity { get; set; }
    public string? DefaultCategory { get; set; }
    public string Status { get; set; } = "active";
    public string RobotsTxtStatus { get; set; } = "unknown";
    public int CrawlDelaySeconds { get; set; } = 5;
    public DateTimeOffset? LastCrawledAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public string? ETag { get; set; }
    public string? LastModifiedHeader { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CrawlerTask
{
    public long Id { get; set; }
    public long SourceId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string City { get; set; } = "Belém";
    public string? Category { get; set; }
    public string State { get; set; } = "pending"; // pending, running, succeeded, failed, skipped_robots
    public int Depth { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CrawlerRunLog
{
    public long Id { get; set; }
    public long SourceId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public int ItemsDiscovered { get; set; }
    public int ItemsIngested { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Informações extraídas de dados estruturados (Schema.org) ou HTML antes da normalização e ingestão.
/// </summary>
public sealed class ExtractedRestaurantData
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? Neighborhood { get; set; }
    public string? Address { get; set; }
    public string? Telephone { get; set; }
    public string? Website { get; set; }
    public string? Category { get; set; }
    public string? ServesCuisine { get; set; }
    public string? PriceRange { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Instagram { get; set; }
    public string? Facebook { get; set; }
    public string? TikTok { get; set; }
    public string? YouTube { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoAttribution { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; } = 0.8;
    public bool HasConflict { get; set; }
    public string? ConflictDetail { get; set; }
}
