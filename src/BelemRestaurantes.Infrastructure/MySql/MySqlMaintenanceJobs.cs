using System.Text.Json;
using Dapper;
namespace BelemRestaurantes.Infrastructure.MySql;
/// <summary>Valida a vigência de dados cadastrados; não inventa atualização de fonte externa.</summary>
public sealed class MySqlMaintenanceJobs(MySqlConnectionFactory factory)
{
    public async Task<long> EnqueueAsync(string kind,string actor,CancellationToken ct)
    {
        if(kind is not("hours" or "mobility"))throw new ArgumentException("Rotina inválida.");
        await using var db=await factory.OpenAsync(ct);
        var name="belrestaurante:maintenance:"+kind;
        if(await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT GET_LOCK(@name,5)",new{name},cancellationToken:ct))!=1)throw new InvalidOperationException("Outra solicitação está sendo preparada.");
        try
        {
            await using var tx=await db.BeginTransactionAsync(ct);
            var id=await db.QueryFirstOrDefaultAsync<long?>(new CommandDefinition("SELECT id FROM operational_maintenance_jobs WHERE kind=@kind AND state IN ('pending','running') ORDER BY id LIMIT 1 FOR UPDATE",new{kind},tx,cancellationToken:ct));
            if(id is null)id=await db.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO operational_maintenance_jobs(kind,actor,next_attempt_at,created_at) VALUES(@kind,@actor,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));SELECT LAST_INSERT_ID();",new{kind,actor},tx,cancellationToken:ct));
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'maintenance.enqueue','maintenance_job',@id,@metadata,UTC_TIMESTAMP(6))",new{actor,id,metadata=JsonSerializer.Serialize(new{kind})},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);return id.Value;
        }
        finally{await db.ExecuteAsync(new CommandDefinition("SELECT RELEASE_LOCK(@name)",new{name},cancellationToken:CancellationToken.None));}
    }
    private sealed class Job{public long Id{get;set;}public string Kind{get;set;}="";public int Attempts{get;set;}}
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var owner=Guid.NewGuid().ToString("N");Job? job;
        await using(var tx=await db.BeginTransactionAsync(ct))
        {
            job=await db.QueryFirstOrDefaultAsync<Job>(new CommandDefinition("""
                SELECT id,kind,attempts FROM operational_maintenance_jobs WHERE
                  (state='pending' AND next_attempt_at<=UTC_TIMESTAMP(6)) OR (state='running' AND locked_until<UTC_TIMESTAMP(6))
                ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
                """,transaction:tx,cancellationToken:ct));
            if(job is not null)await db.ExecuteAsync(new CommandDefinition("UPDATE operational_maintenance_jobs SET state='running',attempts=attempts+1,locked_by=@owner,locked_until=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE) WHERE id=@id",new{owner,id=job.Id},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
        }
        if(job is null)return false;
        try
        {
            await using var tx=await db.BeginTransactionAsync(ct);
            int checkedRecords,needsReview,reviewRequests=0;
            if(job.Kind=="hours")
            {
                checkedRecords=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurant_operation_info",transaction:tx,cancellationToken:ct));
                needsReview=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurant_operation_info WHERE expires_at<=UTC_TIMESTAMP(6) OR confirmed_at>DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE)",transaction:tx,cancellationToken:ct));
                reviewRequests=await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO moderation_queue(item_type,item_id,reason,detail,status,created_at)
                    SELECT 'restaurant',i.restaurant_id,'HoursExpired','Horário vencido ou confirmação futura. Conferir a fonte; dados do parceiro foram preservados.','pending',UTC_TIMESTAMP(6)
                    FROM restaurant_operation_info i WHERE (i.expires_at<=UTC_TIMESTAMP(6) OR i.confirmed_at>DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE))
                    AND NOT EXISTS(SELECT 1 FROM moderation_queue m WHERE m.item_type='restaurant' AND m.item_id=i.restaurant_id AND m.reason='HoursExpired' AND m.status='pending')
                    ORDER BY i.restaurant_id LIMIT 500
                    """,transaction:tx,cancellationToken:ct));
            }
            else if(job.Kind=="mobility")
            {
                checkedRecords=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM mobility_sources WHERE enabled=TRUE",transaction:tx,cancellationToken:ct));
                needsReview=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM mobility_sources WHERE enabled=TRUE AND (expires_at IS NULL OR expires_at<=UTC_TIMESTAMP(6))",transaction:tx,cancellationToken:ct));
                await db.ExecuteAsync(new CommandDefinition("""
                    UPDATE mobility_sources SET checked_at=UTC_TIMESTAMP(6),last_error=CASE WHEN expires_at IS NULL OR expires_at<=UTC_TIMESTAMP(6)
                      THEN 'Referência vencida ou sem vigência. Revisão manual necessária; nenhum feed foi sincronizado.' ELSE last_error END WHERE enabled=TRUE
                    """,transaction:tx,cancellationToken:ct));
            }
            else throw new InvalidOperationException("Rotina desconhecida.");
            var result=JsonSerializer.Serialize(new{kind=job.Kind,checkedRecords,needsReview,reviewRequests,validationOnly=true,externalRequests=0});
            await db.ExecuteAsync(new CommandDefinition("UPDATE operational_maintenance_jobs SET state='completed',result_json=@result,last_error=NULL,locked_until=NULL,locked_by=NULL,completed_at=UTC_TIMESTAMP(6) WHERE id=@id AND locked_by=@owner",new{result,id=job.Id,owner},tx,cancellationToken:ct));
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES('worker:maintenance','maintenance.completed','maintenance_job',@id,@result,UTC_TIMESTAMP(6))",new{id=job.Id,result},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);return true;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE operational_maintenance_jobs SET state=CASE WHEN attempts>=5 THEN 'failed' ELSE 'pending' END,
                next_attempt_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE),last_error=@error,locked_until=NULL,locked_by=NULL
                WHERE id=@id AND locked_by=@owner
                """,new{id=job.Id,owner,error=ex.GetType().Name},cancellationToken:CancellationToken.None));
            return true;
        }
    }
}
