using System.Text.Json;
using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlAdminUserRepository(MySqlConnectionFactory factory) : IAdminUserRepository
{
    public async Task<AdminUserRecord?> FindByNameAsync(string userName, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<AdminRow>(new CommandDefinition(
            """
            SELECT id, user_name AS UserName, email, password_hash AS PasswordHash,
                   two_factor_enabled AS TwoFactorEnabled, two_factor_secret AS Secret,
                   access_failed_count AS AccessFailedCount, lockout_end_at AS LockoutEndAt,must_change_password AS MustChangePassword
            FROM admin_users WHERE user_name = @userName AND is_disabled = FALSE AND disabled = FALSE LIMIT 1
            """,
            new { userName },
            cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<AdminUserRecord?> FindByIdAsync(string id, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<AdminRow>(new CommandDefinition(
            """
            SELECT id, user_name AS UserName, email, password_hash AS PasswordHash,
                   two_factor_enabled AS TwoFactorEnabled, two_factor_secret AS Secret,
                   access_failed_count AS AccessFailedCount, lockout_end_at AS LockoutEndAt,must_change_password AS MustChangePassword
            FROM admin_users WHERE id = @id AND is_disabled = FALSE AND disabled = FALSE LIMIT 1
            """,
            new { id },
            cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<bool> CreateAsync(AdminUserRecord user, string[] roles, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO admin_users (id, user_name, email, password_hash, two_factor_enabled, two_factor_secret)
            VALUES (@Id, @UserName, @Email, @PasswordHash, @TwoFactorEnabled, @Secret)
            """,
            new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.PasswordHash,
                user.TwoFactorEnabled,
                Secret = user.TwoFactorSecret
            },
            transaction,
            cancellationToken: ct));

        foreach (var role in roles)
        {
            var roleId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT id FROM admin_roles WHERE name = @name LIMIT 1",
                new { name = role },
                transaction,
                cancellationToken: ct));

            if (roleId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT IGNORE INTO admin_user_roles (user_id, role_id) VALUES (@userId, @roleId)",
                    new { userId = user.Id, roleId },
                    transaction,
                    cancellationToken: ct));
            }
        }

        await transaction.CommitAsync(ct);
        return inserted > 0;
    }

    public async Task IncrementFailuresAsync(string id, DateTimeOffset until, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE admin_users
            SET lockout_end_at = CASE WHEN access_failed_count + 1 >= 5 THEN @until ELSE lockout_end_at END,
                access_failed_count = access_failed_count + 1
            WHERE id = @id
            """,
            new { id, until },
            cancellationToken: ct));
    }

    public async Task ResetFailuresAsync(string id, DateTimeOffset lastLogin, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE admin_users
            SET access_failed_count = 0, lockout_end_at = NULL, last_login_at = @lastLogin
            WHERE id = @id
            """,
            new { id, lastLogin },
            cancellationToken: ct));
    }

    public async Task<bool> EnableTwoFactorAsync(string id, byte[] encryptedSecret, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE admin_users SET two_factor_enabled = TRUE, two_factor_secret = @secret WHERE id = @id AND two_factor_enabled = FALSE AND disabled = FALSE AND is_disabled = FALSE AND (lockout_end_at IS NULL OR lockout_end_at <= UTC_TIMESTAMP(6))",
            new { id, secret = encryptedSecret },
            cancellationToken: ct)) == 1;
    }

    public async Task<bool> UpdateTwoFactorSecretAsync(string id, byte[] encryptedSecret, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE admin_users SET two_factor_secret = @secret WHERE id = @id AND disabled = FALSE AND is_disabled = FALSE",
            new { id, secret = encryptedSecret },
            cancellationToken: ct)) == 1;
    }

    public async Task<bool> UpdatePasswordAsync(string id, string passwordHash, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE admin_users
            SET password_hash = @passwordHash,
                must_change_password = FALSE,
                password_changed_at = UTC_TIMESTAMP(6),
                access_failed_count = 0,
                lockout_end_at = NULL
            WHERE id = @id AND disabled = FALSE AND is_disabled = FALSE
            """,
            new { id, passwordHash },
            cancellationToken: ct)) == 1;
    }

    public async Task<bool> ConsumeTotpAsync(string id, long counter, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE admin_users SET last_totp_counter = @counter WHERE id = @id AND last_totp_counter < @counter AND two_factor_enabled = TRUE AND disabled = FALSE AND is_disabled = FALSE AND (lockout_end_at IS NULL OR lockout_end_at <= UTC_TIMESTAMP(6))",
            new { id, counter }, cancellationToken: ct)) == 1;
    }

    public async Task<string[]> GetRolesAsync(string id, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT r.name FROM admin_roles r
            JOIN admin_user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @id
            """,
            new { id },
            cancellationToken: ct));
        return rows.AsList().ToArray();
    }

    private static AdminUserRecord Map(AdminRow row) => new(
        row.Id,
        row.UserName,
        row.Email,
        row.PasswordHash,
        row.TwoFactorEnabled,
        row.Secret,
        row.AccessFailedCount,
        row.LockoutEndAt,
        null) { MustChangePassword=row.MustChangePassword };

    private sealed class AdminRow
    {
        public bool MustChangePassword { get; set; }
        public string Id { get; set; } = default!;
        public string UserName { get; set; } = default!;
        public string? Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public bool TwoFactorEnabled { get; set; } = default!;
        public byte[]? Secret { get; set; } = default!;
        public int AccessFailedCount { get; set; } = default!;
        public DateTimeOffset? LockoutEndAt { get; set; } = default!;
    }
}
