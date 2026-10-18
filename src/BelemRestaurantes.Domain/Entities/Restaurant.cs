using BelemRestaurantes.Domain.Enums;

namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Estabelecimento do catálogo. Toda linha nasce em <see cref="RestaurantStatus.Pending"/>:
/// a automação nunca publica sozinha.
/// </summary>
public sealed class Restaurant
{
    // Materialização do catálogo pelo Dapper; construção pública continua
    // passando pelas validações do construtor de domínio.
    private Restaurant() { }

    public long Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Nome normalizado para comparação/deduplicação.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string? Neighborhood { get; private set; }

    public string? Category { get; private set; }

    public string? Address { get; private set; }

    public string? Website { get; private set; }
    public string? Instagram { get; private set; }
    public string? Facebook { get; private set; }
    public string? TikTok { get; private set; }
    public string? YouTube { get; private set; }

    /// <summary>Foto aprovada para exibição, com autorização registrada.</summary>
    public string? PhotoUrl { get; private set; }

    public RestaurantStatus Status { get; private set; } = RestaurantStatus.Pending;

    /// <summary>Identificador do Place ID na origem; é a chave de deduplicação mais confiável.</summary>
    public string? GooglePlaceId { get; private set; }

    public string? CreatedSourceUrl { get; private set; }

    public RestaurantStatus StatusBeforeSuspend { get; private set; } = RestaurantStatus.Published;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>Parâmetros de construção. A mutação de status é restrita a métodos de regra.</summary>
    public Restaurant(
        long id,
        string name,
        string normalizedName,
        string city,
        string? neighborhood,
        string? category,
        string? address,
        string? googlePlaceId,
        string? createdSourceUrl,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        RestaurantStatus status = RestaurantStatus.Pending,
        DateTimeOffset? lastSeenAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome do estabelecimento é obrigatório.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("Cidade é obrigatória.", nameof(city));
        }

        Id = id;
        Name = name.Trim();
        NormalizedName = normalizedName;
        City = city.Trim();
        Neighborhood = NeighborhoodRules.Clean(neighborhood);
        Category = CategoryRules.Clean(category);
        Address = AddressRules.Clean(address);
        GooglePlaceId = string.IsNullOrWhiteSpace(googlePlaceId) ? null : googlePlaceId.Trim();
        CreatedSourceUrl = createdSourceUrl;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        LastSeenAt = lastSeenAt;
        Status = status;
    }

    /// <summary>
    /// Só <c>Pending</c> pode ir para <c>Published</c>. Os demais estados já publicados
    /// são ignorados para evitar que uma nova varredura re-publique algo vetado.
    /// </summary>
    public bool TryApprove(DateTimeOffset now)
    {
        if (Status is not RestaurantStatus.Pending)
        {
            return false;
        }

        Status = RestaurantStatus.Published;
        UpdatedAt = now;
        return true;
    }

    public bool TryReject(DateTimeOffset now)
    {
        if (Status is RestaurantStatus.Published)
        {
            return false;
        }

        Status = RestaurantStatus.Rejected;
        UpdatedAt = now;
        return true;
    }

    public bool TrySuspend(DateTimeOffset now)
    {
        if (Status is not RestaurantStatus.Published)
        {
            return false;
        }

        StatusBeforeSuspend = Status;
        Status = RestaurantStatus.Suspended;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Aplica dados observados numa nova varredura. O status NUNCA é tocado aqui:
    /// apenas o campo observado e as opções de contato aprovadas mudam.
    /// </summary>
    public void ApplyObservation(
        string name,
        string normalizedName,
        string? neighborhood,
        string? category,
        string? address,
        DateTimeOffset now)
    {
        Name = name.Trim();
        NormalizedName = normalizedName;
        Neighborhood = NeighborhoodRules.Clean(neighborhood);
        Category = CategoryRules.Clean(category);
        Address = AddressRules.Clean(address);
        LastSeenAt = now;
        UpdatedAt = now;
    }

    /// <summary>Associa um perfil de rede social. Só URLs http(s) são aceitas.</summary>
    public void AttachSocialProfile(SocialNetwork network, string url)
    {
        if (!SocialLinkRules.TryValidate(url, out var safe))
        {
            throw new ArgumentException("Link de rede social inválido.", nameof(url));
        }

        switch (network)
        {
            case SocialNetwork.Instagram: Instagram = safe; break;
            case SocialNetwork.Facebook: Facebook = safe; break;
            case SocialNetwork.TikTok: TikTok = safe; break;
            case SocialNetwork.YouTube: YouTube = safe; break;
            default: throw new ArgumentOutOfRangeException(nameof(network), network, "Rede social não suportada.");
        }

        UpdatedAt = LastSeenAt ?? UpdatedAt;
    }
}
