namespace BelemRestaurantes.Domain.Identity;

/// <summary>Papéis administrativos. Nenhum acesso sem papel.</summary>
public static class AdminRoles
{
    /// <summary>Operação diária: aprova, reprova, modera.</summary>
    public const string Moderator = "restaurant_moderator";

    /// <summary>Configura fontes, conectores, limites e pausa integrações.</summary>
    public const string Operator = "platform_operator";

    /// <summary>Tudo, inclusive gestão de contas.</summary>
    public const string PlatformAdmin = "platform_admin";

    public static readonly IReadOnlyList<string> All = [Moderator, Operator, PlatformAdmin];

    /// <summary>Papéis que podem aprovar conteúdo para publicação.</summary>
    public static readonly IReadOnlyList<string> CanModerate = [Moderator, PlatformAdmin];

    /// <summary>Papéis que podem alterar configuração de integrações e limites.</summary>
    public static readonly IReadOnlyList<string> CanConfigure = [Operator, PlatformAdmin];
}

/// <summary>
/// Usuário administrativo. Mapeia para <c>Microsoft.AspNetCore.Identity.IdentityUser</c>
/// na camada Web; aqui só a forma de domínio, sem dependência do framework.
/// </summary>
public sealed class AdminUser
{
    public string Id { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }

    /// <summary>Segredo TOTP em base32, guardado cifrado em repouso pela camada de infraestrutura.</summary>
    public string? TwoFactorSecret { get; init; }

    public DateTimeOffset? LockoutEndAt { get; init; }
    public int AccessFailedCount { get; init; }
    public bool LockoutEnabled { get; init; } = true;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndAt is not null && LockoutEndAt > now;

    public bool RequiresMfa =>
        // Conta de serviço e papel administrativo nunca operam sem segundo fator.
        !string.IsNullOrEmpty(TwoFactorSecret);
}