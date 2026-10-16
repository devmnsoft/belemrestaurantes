namespace BelemRestaurantes.Application.Abstractions;

public static class MemberAuth
{
    public const string Scheme = "BelRest.Member";
    public const string Policy = "VerifiedMember";
}

public sealed record RegisterMember(string FullName, string Email, string Password, bool AcceptTerms, string Culture);
public sealed record MemberSession(string UserId, string Name, string Email, string Culture, string SessionToken, DateTimeOffset ExpiresAt);
public sealed record MemberSessionView(string Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Current);

public sealed record MemberProfile(string UserId, string FullName, string Email, bool EmailConfirmed, string? Bio, bool IsBlocked, DateTimeOffset CreatedAt);
public sealed record MemberAdminView(string Id, string FullName, string Email, bool EmailConfirmed, bool IsBlocked, int FailedAttempts, DateTimeOffset? LockoutUntil, DateTimeOffset CreatedAt);

public interface IMemberAccounts
{
    Task RegisterAsync(RegisterMember input, CancellationToken ct);
    Task<MemberSession?> LoginAsync(string email, string password, bool remember, CancellationToken ct);
    Task<MemberSession?> ValidateSessionAsync(string token, CancellationToken ct);
    Task RevokeSessionAsync(string userId, string token, CancellationToken ct);
    Task<IReadOnlyList<MemberSessionView>> SessionsAsync(string userId, string currentToken, CancellationToken ct);
    Task RevokeOtherSessionsAsync(string userId, string currentToken, CancellationToken ct);
    Task RequestTokenAsync(string email, string purpose, string culture, CancellationToken ct);
    Task<bool> ConfirmEmailAsync(string token, CancellationToken ct);
    Task<bool> ResetPasswordAsync(string token, string password, CancellationToken ct);

    // Sprint 04 - Identidade e Perfil do Visitante
    Task<MemberProfile?> GetProfileAsync(string userId, CancellationToken ct);
    Task<bool> UpdateProfileAsync(string userId, string fullName, string? bio, CancellationToken ct);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct);
    Task RequestEmailChangeAsync(string userId, string newEmail, string culture, CancellationToken ct);
    Task<bool> ConfirmEmailChangeAsync(string token, CancellationToken ct);
    Task<bool> LockAccountAsync(string userId, string reason, CancellationToken ct);
    Task<bool> UnlockAccountAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<MemberAdminView>> ListMembersAdminAsync(int page, int pageSize, string? search, CancellationToken ct);
}

public sealed record TransactionalMail(string Recipient, string Name, string Kind, string? Link, string Culture);
public interface ITransactionalMailSender
{
    Task SendAsync(string messageId, TransactionalMail message, CancellationToken ct);
}
public interface IEmailDispatcher
{
    Task<int> DispatchAsync(int limit, CancellationToken ct);
}
