using Dapper;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlFavoriteRepository(MySqlConnectionFactory factory) : IFavoriteRepository
{
    public async Task<IReadOnlyList<long>> ListAsync(string userSubject, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT restaurant_id FROM favorites WHERE user_subject = @u ORDER BY created_at DESC",
            new { u = userSubject }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> AddAsync(string userSubject, long restaurantId, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var n = await connection.ExecuteAsync(new CommandDefinition(
            "INSERT IGNORE INTO favorites (user_subject, restaurant_id) VALUES (@u, @r)",
            new { u = userSubject, r = restaurantId }, cancellationToken: ct));
        return n > 0;
    }

    public async Task<bool> RemoveAsync(string userSubject, long restaurantId, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var n = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM favorites WHERE user_subject=@u AND restaurant_id=@r",
            new { u = userSubject, r = restaurantId }, cancellationToken: ct));
        return n > 0;
    }

    public async Task<bool> IsFavoriteAsync(string userSubject, long restaurantId, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM favorites WHERE user_subject=@u AND restaurant_id=@r",
            new { u = userSubject, r = restaurantId }, cancellationToken: ct));
        return count > 0;
    }
}

public sealed class MySqlRatingRepository(MySqlConnectionFactory factory) : IRatingRepository
{
    public async Task<long> UpsertAsync(string userSubject, long restaurantId, int stars, string? review, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ratings (restaurant_id, user_subject, stars, review_text, status)
            VALUES (@restaurantId, @user, @stars, @review, 'pending')
            ON DUPLICATE KEY UPDATE
                stars = VALUES(stars),
                review_text = VALUES(review_text),
                status = 'pending',
                updated_at = UTC_TIMESTAMP(6)
            """,
            new { restaurantId, user = userSubject, stars, review },
            transaction: tx,
            cancellationToken: ct));

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT id FROM ratings WHERE restaurant_id=@r AND user_subject=@u",
            new { r = restaurantId, u = userSubject },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<RestaurantRatingView>> ListApprovedAsync(long restaurantId, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<RatingRow>(new CommandDefinition(
            "SELECT id, user_subject AS UserSubject, stars, review_text AS Review, created_at AS CreatedAt FROM ratings WHERE restaurant_id=@r AND status='approved' ORDER BY created_at DESC LIMIT 100",
            new { r = restaurantId }, cancellationToken: ct));
        return rows.Select(r => new RestaurantRatingView(r.Id, r.UserSubject, r.Stars, r.Review, r.CreatedAt)).AsList();
    }

    public async Task<double?> AverageAsync(long restaurantId, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var avg = await connection.ExecuteScalarAsync<object?>(new CommandDefinition(
            "SELECT AVG(stars) FROM ratings WHERE restaurant_id=@r AND status='approved'",
            new { r = restaurantId }, cancellationToken: ct));
        return avg is null || avg.ToString() == string.Empty ? null : Convert.ToDouble(avg, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record RatingRow(long Id, string UserSubject, int Stars, string? Review, DateTimeOffset CreatedAt);
}

public sealed class MySqlDeletionRequestRepository(MySqlConnectionFactory factory) : IDeletionRequestRepository
{
    public async Task<long> CreateAsync(string requesterSubject, string targetType, long targetId, string? reason, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO deletion_requests (requester_subject, target_type, target_id, reason) VALUES (@s, @t, @id, @r)",
            new { s = requesterSubject, t = targetType, id = targetId, r = reason }, cancellationToken: ct));
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DeletionRequestView>> ListPendingAsync(CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<DeletionRow>(new CommandDefinition(
            "SELECT id, requester_subject AS Requester, target_type AS TargetType, target_id AS TargetId, reason AS Reason, status AS Status, requested_at AS RequestedAt FROM deletion_requests WHERE status='pending' ORDER BY requested_at DESC",
            cancellationToken: ct));
        return rows.Select(r => new DeletionRequestView(r.Id, r.Requester, r.TargetType, r.TargetId, r.Reason, r.Status, r.RequestedAt)).AsList();
    }

    public async Task CompleteAsync(long id, string actor, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE deletion_requests SET status='completed', completed_at=UTC_TIMESTAMP(6) WHERE id=@id",
            new { id }, cancellationToken: ct));
    }

    public async Task RejectAsync(long id, string actor, string reason, CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE deletion_requests SET status='rejected', completed_at=UTC_TIMESTAMP(6) WHERE id=@id",
            new { id }, cancellationToken: ct));
    }

    private sealed record DeletionRow(long Id, string Requester, string TargetType, long TargetId, string? Reason, string Status, DateTimeOffset RequestedAt);
}