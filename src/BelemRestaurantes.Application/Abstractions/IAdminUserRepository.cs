namespace BelemRestaurantes.Application.Abstractions;

public interface IAdminUserRepository
{
    Task<AdminUserRecord?> FindByNameAsync(string userName, CancellationToken ct);

    Task<AdminUserRecord?> FindByIdAsync(string id, CancellationToken ct);

    Task<bool> CreateAsync(AdminUserRecord user, string[] roles, CancellationToken ct);

    Task IncrementFailuresAsync(string id, DateTimeOffset until, CancellationToken ct);

    Task ResetFailuresAsync(string id, DateTimeOffset lastLogin, CancellationToken ct);

    Task<bool> EnableTwoFactorAsync(string id, byte[] encryptedSecret, CancellationToken ct);

    Task<bool> ConsumeTotpAsync(string id, long counter, CancellationToken ct);

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
    /// <summary>Bloqueio por tentativas falhas vigente.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndAt is not null && LockoutEndAt > now;
}