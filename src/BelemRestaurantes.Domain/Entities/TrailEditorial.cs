namespace BelemRestaurantes.Domain.Entities;

public sealed record TrailEditorial(string Audience, string Origin, string Mode, string BeforeYouGo,
    string Accessibility, IReadOnlyList<EditorialTrailStop> Stops);
public sealed record EditorialTrailStop(string Name, string City, string Summary, string SourceUrl, string SourceName);
