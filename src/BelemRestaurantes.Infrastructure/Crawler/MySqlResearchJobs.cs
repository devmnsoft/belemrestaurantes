using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;
public sealed class MySqlResearchJobs(MySqlConnectionFactory factory):IResearchJobs
{
    public async Task<IReadOnlyList<ResearchJobStatus>> RecentAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);return (await db.QueryAsync<ResearchJobStatus>(new CommandDefinition("SELECT id Id,city City,state State,last_status LastStatus,source_count SourceCount,queued_count QueuedCount,created_at CreatedAt FROM research_jobs ORDER BY id DESC LIMIT 30",cancellationToken:ct))).ToArray();
    }
    public async Task EnqueueAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO research_jobs(target_id,run_day,city,query_text)
            SELECT id,UTC_DATE(),city,CONCAT('restaurantes ',category,' ',city,' Pará endereço cardápio site oficial')
            FROM discovery_targets t WHERE enabled=1 AND NOT EXISTS(SELECT 1 FROM research_jobs r WHERE r.target_id=t.id AND r.run_day=UTC_DATE()) ORDER BY next_run_at,id LIMIT 2
            """,cancellationToken:ct));
    }
    public async Task<ResearchJob?> ClaimAsync(CancellationToken ct,long? targetId=null)
    {
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        // Recheck city/category pause while claiming. Expired leases can resume after a crash.
        var row=await db.QuerySingleOrDefaultAsync<ResearchJob>(new CommandDefinition("""
            SELECT CAST(r.id AS SIGNED) Id,r.city City,r.query_text `Query` FROM research_jobs r
            INNER JOIN discovery_targets t ON t.id=r.target_id AND t.enabled=1
            WHERE (@targetId IS NULL OR r.target_id=@targetId) AND (r.state='pending' OR (r.state='running' AND r.lease_until<UTC_TIMESTAMP(6)))
            ORDER BY r.id LIMIT 1 FOR UPDATE SKIP LOCKED
            """,new{targetId},transaction:tx,cancellationToken:ct));
        if(row is not null)await db.ExecuteAsync(new CommandDefinition("UPDATE research_jobs SET state='running',lease_until=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 3 MINUTE) WHERE id=@Id",row,tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return row;
    }
    public async Task CompleteAsync(long id,string status,int sources,int queued,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);await db.ExecuteAsync(new CommandDefinition("UPDATE research_jobs SET state='completed',last_status=@status,source_count=@sources,queued_count=@queued,completed_at=UTC_TIMESTAMP(6),lease_until=NULL WHERE id=@id AND state='running'",new{id,status,sources,queued},cancellationToken:ct));
    }
}
