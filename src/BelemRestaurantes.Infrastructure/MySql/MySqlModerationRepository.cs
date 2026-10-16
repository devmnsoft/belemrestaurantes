using Dapper;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Decisões de moderação. É o ÚNICO lugar do sistema que muda
/// <c>restaurants.status</c>. Toda transição grava evento de auditoria na
/// mesma transação, garantindo que não exista mudança sem rastro.
/// </summary>
public sealed class MySqlModerationRepository(
    MySqlConnectionFactory factory,
    IClock clock) : IModerationRepository
{
    public async Task<PagedResult<PendingApproval>> ListPendingAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var connection = await factory.OpenAsync(ct);

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM restaurants WHERE status = 'pending'", cancellationToken: ct));

        if (total == 0)
        {
            return PagedResult<PendingApproval>.Empty(page, pageSize);
        }

        var items = await connection.QueryAsync<PendingApproval>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.name AS Name, r.city AS City, r.neighborhood AS Neighborhood,
                   r.category AS Category, r.google_place_id AS GooglePlaceId,
                   r.created_source_url AS SourceUrl, r.created_at AS DiscoveredAt,
                   COALESCE(m.detail, '') AS ConflictReason,
                   NULL AS DedupConfidence, NULL AS AiSuggestion
            FROM restaurants r
            LEFT JOIN moderation_queue m
                   ON m.item_type = 'restaurant' AND m.item_id = r.id AND m.status = 'pending'
            WHERE r.status = 'pending'
            ORDER BY r.created_at
            LIMIT @pageSize OFFSET @offset
            """,
            new { pageSize, offset },
            cancellationToken: ct));

        return new PagedResult<PendingApproval>(items.AsList(), page, pageSize, total);
    }

    public async Task<bool> ApproveAsync(long restaurantId, string actor, CancellationToken ct) =>
        await TransitionAsync(
            restaurantId,
            from: "pending",
            to: "published",
            action: "restaurant.approve",
            actor,
            reason: null,
            ct);

    public async Task<bool> RejectAsync(long restaurantId, string actor, string reason, CancellationToken ct) =>
        await TransitionAsync(
            restaurantId,
            from: "pending",
            to: "rejected",
            action: "restaurant.reject",
            actor,
            reason,
            ct);

    public async Task<bool> SuspendAsync(long restaurantId, string actor, string reason, CancellationToken ct) =>
        await TransitionAsync(
            restaurantId,
            from: "published",
            to: "suspended",
            action: "restaurant.suspend",
            actor,
            reason,
            ct);

    private async Task<bool> TransitionAsync(
        long restaurantId,
        string from,
        string to,
        string action,
        string actor,
        string? reason,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // A condição `status = @from` na própria cláusula WHERE torna a operação
        // idempotente e impede corrida entre dois moderadores (o segundo pega 0 linhas).
        var sql = $"""
            UPDATE restaurants
            SET status = @to,
                published_at = CASE WHEN @to = 'published' THEN @now ELSE published_at END,
                status_before_suspend = CASE WHEN @to = 'suspended' THEN status ELSE status_before_suspend END
            WHERE id = @id AND status = @from
            """;

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { to, now, id = restaurantId, from },
            transaction,
            cancellationToken: ct));

        if (changed == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            from,
            to,
            reason,
            at = now
        });

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, metadata, occurred_at)
            VALUES (@actor, @action, 'restaurant', @id, @metadata, @now)
            """,
            new { actor, action, id = restaurantId, metadata, now },
            transaction,
            cancellationToken: ct));

        // Fechar as pendências abertas daquele item: a decisão já foi tomada.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE moderation_queue
            SET status = CASE WHEN @to = 'published' THEN 'approved' ELSE 'rejected' END,
                reviewed_at = @now, reviewed_by = @actor
            WHERE item_type = 'restaurant' AND item_id = @id AND status = 'pending'
            """,
            new { to, now, actor, id = restaurantId },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task EnqueueAsync(
        string itemType,
        long itemId,
        ModerationReason reason,
        string detail,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO moderation_queue (item_type, item_id, reason, detail, status, created_at)
            SELECT @itemType, @itemId, @reason, @detail, 'pending', @now
            FROM DUAL
            WHERE NOT EXISTS (
                SELECT 1 FROM moderation_queue
                WHERE item_type = @itemType AND item_id = @itemId AND reason = @reason AND status = 'pending'
            )
            """;

        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            itemType,
            itemId,
            reason = reason.ToString(),
            detail = detail.Length > 500 ? detail[..500] : detail,
            now = clock.UtcNow
        }, cancellationToken: ct));
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM restaurants WHERE status = 'pending'", cancellationToken: ct));
    }
}

/// <summary>Escrita da trilha de auditoria. Usada também fora das transações de moderação.</summary>
public sealed class MySqlAuditWriter(MySqlConnectionFactory factory, IClock clock) : IAuditWriter
{
    public async Task WriteAsync(
        string action,
        string entityType,
        long? entityId,
        string? metadataJson,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, metadata, occurred_at)
            VALUES (@actor, @action, @entityType, @entityId, @metadata, @now)
            """,
            new
            {
                actor = CurrentActor.Value,
                action,
                entityType,
                entityId,
                metadata = metadataJson,
                now = clock.UtcNow
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// Ator corrente propagado pelo pipeline de requisição. No worker, o valor
    /// é o identificador da instância do serviço.
    /// </summary>
    public static readonly AsyncLocal<string?> CurrentActor = new();
}