namespace BelemRestaurantes.Web.Domain;
public sealed record Restaurant(long Id,string Name,string City,string? Neighborhood,string? Category,string? Address,string? Website,string? Instagram,string? Facebook,string? TikTok,string? Youtube,string? PhotoUrl,string Status,DateTimeOffset UpdatedAt);
public sealed record RestaurantSuggestion(string Name,string City,string? Website,string? Notes);