using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Social;

public sealed class MySqlLgpdService(MySqlConnectionFactory factory, IExperienceService experienceService, ILogger<MySqlLgpdService> logger) : ILgpdService
{
    public async Task<long> RequestExportAsync(string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO lgpd_requests (user_id, request_type, status, requested_at)
            VALUES (@userId, 'export', 'pending', UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<LgpdExportData> BuildExportDataAsync(string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var user = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            "SELECT id, full_name, email, created_at FROM member_users WHERE id = @userId",
            new { userId }, cancellationToken: ct))
            ?? throw new KeyNotFoundException("Usuário não encontrado.");

        var sessions = await connection.QueryAsync<MemberSessionView>(new CommandDefinition(
            "SELECT id_hash AS Id, created_at AS CreatedAt, expires_at AS ExpiresAt, FALSE AS Current FROM member_sessions WHERE user_id = @userId ORDER BY created_at DESC",
            new { userId }, cancellationToken: ct));

        var comments = await connection.QueryAsync<CommentView>(new CommandDefinition(
            "SELECT id, experience_id AS ExperienceId, user_id AS UserId, author_name AS AuthorName, comment_text AS CommentText, created_at AS CreatedAt FROM experience_comments WHERE user_id = @userId ORDER BY created_at DESC",
            new { userId }, cancellationToken: ct));

        var favorites = (await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT restaurant_id FROM favorites WHERE user_subject = @userId ORDER BY created_at DESC",
            new { userId }, cancellationToken: ct))).AsList();

        var experiences = await experienceService.ListUserExperiencesAsync(userId, ct);

        return new LgpdExportData(
            user.id,
            user.full_name,
            user.email,
            user.created_at,
            sessions.AsList(),
            experiences,
            comments.AsList(),
            favorites,
            DateTimeOffset.UtcNow
        );
    }

    public async Task<long> RequestDeletionAsync(string userId, string? reason, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO lgpd_requests (user_id, request_type, status, notes, requested_at)
            VALUES (@userId, 'deletion', 'pending', @reason, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { userId, reason }, cancellationToken: ct));
    }

    public async Task<bool> AnonymizeAccountAsync(string userId, string? adminActor, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var anonEmail = $"anon_{Guid.NewGuid():N}@anon.belemrestaurantes.com.br";
        var now = DateTimeOffset.UtcNow;

        const string userSql = """
            UPDATE member_users
            SET full_name = 'Usuário Anonimizado',
                email = @anonEmail,
                normalized_email = @normalized,
                bio = NULL,
                is_anonymized = TRUE,
                anonymized_at = UTC_TIMESTAMP(6),
                is_blocked = TRUE,
                security_stamp = @newStamp
            WHERE id = @userId
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(userSql, new
        {
            userId,
            anonEmail,
            normalized = anonEmail.ToUpperInvariant(),
            newStamp = Guid.NewGuid().ToString("N")
        }, tx, cancellationToken: ct));

        if (affected > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE member_sessions SET revoked_at = UTC_TIMESTAMP(6) WHERE user_id = @userId AND revoked_at IS NULL",
                new { userId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE member_tokens SET used_at = UTC_TIMESTAMP(6) WHERE user_id = @userId AND used_at IS NULL",
                new { userId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, occurred_at)
                VALUES (@actor, 'member.anonymize_lgpd', 'member_user', @userId, UTC_TIMESTAMP(6))
                """, new { actor = adminActor ?? userId, userId }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("Conta de usuário {UserId} anonimizada sob termos da LGPD.", userId);
        return affected > 0;
    }

    public async Task<IReadOnlyList<LgpdRequest>> ListPendingRequestsAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id, user_id AS UserId, request_type AS RequestType, status,
                   notes, payload_export AS PayloadExport, requested_at AS RequestedAt,
                   completed_at AS CompletedAt
            FROM lgpd_requests
            WHERE status = 'pending'
            ORDER BY requested_at ASC
            """;

        var rows = await connection.QueryAsync<LgpdRequest>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> ReviewRequestAsync(long requestId, bool complete, string? notes, string adminActor, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = complete ? "completed" : "rejected";
        const string sql = """
            UPDATE lgpd_requests
            SET status = @status, notes = @notes, completed_at = UTC_TIMESTAMP(6)
            WHERE id = @requestId
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { requestId, status, notes }, tx, cancellationToken: ct));

        if (affected > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, occurred_at)
                VALUES (@adminActor, @action, 'lgpd_request', @requestId, UTC_TIMESTAMP(6))
                """, new { adminActor, action = "lgpd." + status, requestId }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    private sealed class UserRow
    {
        public string id { get; set; } = "";
        public string full_name { get; set; } = "";
        public string email { get; set; } = "";
        public DateTimeOffset created_at { get; set; }
    }
}
