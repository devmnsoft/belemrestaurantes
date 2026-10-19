namespace BelemRestaurantes.Application.Abstractions;

public interface IAdminUserRepository
{
    Task<AdminUserRecord?> FindByNameAsync(string userName, CancellationToken ct);

    Task<AdminUserRecord?> FindByIdAsync(string id, CancellationToken ct);

    Task<bool> CreateAsync(AdminUserRecord user, string[] roles, CancellationToken ct);

    Task IncrementFailuresAsync(string id, DateTimeOffset until, CancellationToken ct);

    Task ResetFailuresAsync(string id, DateTimeOffset lastLogin, CancellationToken ct);

    Task<bool> EnableTwoFactorAsync(string id, byte[] encryptedSecret, CancellationToken ct);

    Task<bool> UpdateTwoFactorSecretAsync(string id, byte[] encryptedSecret, CancellationToken ct);

    Task<bool> UpdatePasswordAsync(string id, string passwordHash, CancellationToken ct);

    Task<bool> ConsumeTotpAsync(string id, long counter, CancellationToken ct);

    /// <summary>Substitui apenas códigos ainda não usados. O valor guardado é o hash.</summary>
    Task ReplaceRecoveryCodesAsync(string id, IReadOnlyList<string> codeHashes, CancellationToken ct);

    /// <summary>Consome um hash de uso único e marca a troca do autenticador. Não desliga o MFA.</summary>
    Task<bool> ConsumeRecoveryCodeAsync(string id, string codeHash, CancellationToken ct);

    /// <summary>Grava o novo segredo cifrado, novos hashes e encerra a rotação. O MFA continua ligado.</summary>
    Task<bool> RotateMfaAsync(string id, byte[] encryptedSecret, IReadOnlyList<string> codeHashes, CancellationToken ct);

    Task<string[]> GetRolesAsync(string id, CancellationToken ct);
}

public sealed record AdminUserRecord(
    string Id,
    string UserName,
    string? Email,
    string PasswordHash,
    bool TwoFactorEnabled,
    byte[]? TwoFactorSecret,
    int AccessFailedCount,
    DateTimeOffset? LockoutEndAt,
    DateTimeOffset? LastLoginAt)
{
    public bool MustChangePassword { get; init; }

    /// <summary>O segundo fator continua obrigatório; falta confirmar o autenticador novo.</summary>
    public bool MustRotateMfa { get; init; }
    /// <summary>Bloqueio por tentativas falhas vigente.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndAt is not null && LockoutEndAt > now;
}
