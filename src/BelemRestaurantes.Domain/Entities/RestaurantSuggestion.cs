namespace BelemRestaurantes.Domain.Entities;

/// <summary>Redes sociais com conector oficial autorizado.</summary>
public enum SocialNetwork
{
    Instagram = 1,
    Facebook = 2,
    TikTok = 3,
    YouTube = 4
}

/// <summary>
/// Sugestão enviada por um visitante pelo site. Sempre entra na moderação.
/// </summary>
public sealed class RestaurantSuggestion
{
    public long Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string? Website { get; private set; }
    public string? Notes { get; private set; }
    public SuggestionStatus Status { get; private set; } = SuggestionStatus.Pending;
    public DateTimeOffset SubmittedAt { get; private set; }

    public RestaurantSuggestion(
        long id,
        string name,
        string city,
        string? website,
        string? notes,
        DateTimeOffset submittedAt,
        SuggestionStatus status = SuggestionStatus.Pending)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome é obrigatório.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        City = city.Trim();
        Website = SocialLinkRules.TryValidate(website, out var safe) ? safe : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SubmittedAt = submittedAt;
        Status = status;
    }
}

public enum SuggestionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}