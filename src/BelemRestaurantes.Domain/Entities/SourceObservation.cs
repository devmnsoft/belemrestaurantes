using BelemRestaurantes.Domain.Enums;

namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Rastro de proveniência: liga um estabelecimento a um ID externo numa fonte,
/// guardando quando foi consultado e quão confiável é o dado. É o que permite
/// exibir "origem e data da atualização" exigidas pela UX.
/// </summary>
public sealed class SourceObservation
{
    public long Id { get; private set; }
    public long RestaurantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string? SourceUrl { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? LastFetchedAt { get; private set; }
    public DataQuality DataQuality { get; private set; }

    /// <summary>Versão do payload Seen, para detectar mudança na fonte.</summary>
    public string? PayloadHash { get; private set; }

    public SourceObservation(
        long id,
        long restaurantId,
        string provider,
        string externalId,
        string? sourceUrl,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DataQuality dataQuality = DataQuality.Unverified,
        DateTimeOffset? lastFetchedAt = null,
        string? payloadHash = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provedor é obrigatório.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("Identificador externo é obrigatório.", nameof(externalId));
        }

        Id = id;
        RestaurantId = restaurantId;
        Provider = provider.Trim();
        ExternalId = externalId.Trim();
        SourceUrl = sourceUrl;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        DataQuality = dataQuality;
        LastFetchedAt = lastFetchedAt;
        PayloadHash = payloadHash;
    }

    public void Refresh(DateTimeOffset now, string payloadHash, DataQuality quality)
    {
        LastSeenAt = now;
        LastFetchedAt = now;
        PayloadHash = payloadHash;
        DataQuality = quality;
    }

    public bool HasPayloadChanged(string payloadHash) =>
        PayloadHash is not null && !string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal);
}

/// <summary>Item aguardando decisão de um operador.</summary>
public sealed class ModerationQueueItem
{
    public long Id { get; private set; }
    public string ItemType { get; private set; } = string.Empty;
    public long ItemId { get; private set; }
    public ModerationReason Reason { get; private set; }
    public string Detail { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedBy { get; private set; }

    public ModerationQueueItem(
        long id,
        string itemType,
        long itemId,
        ModerationReason reason,
        string detail,
        DateTimeOffset createdAt)
    {
        Id = id;
        ItemType = itemType;
        ItemId = itemId;
        Reason = reason;
        Detail = detail.Length > 240 ? detail[..240] : detail;
        CreatedAt = createdAt;
    }
}