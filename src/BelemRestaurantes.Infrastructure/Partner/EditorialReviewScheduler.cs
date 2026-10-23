using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Partner;

public sealed class EditorialReviewScheduler(MySqlConnectionFactory factory)
{
    public async Task<int> EnqueueDueAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO editorial_review_queue(trail_id,version) SELECT id,editorial_version FROM gastronomic_trails WHERE editorial_json IS NOT NULL AND is_active=TRUE AND editorial_state='published' AND reviewed_at<DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 90 DAY)",cancellationToken:ct));
    }
}
