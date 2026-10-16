using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Members;

public sealed class MySqlEmailDispatcher(MySqlConnectionFactory factory,MailPayloadProtector protector,ITransactionalMailSender sender,IClock clock) : IEmailDispatcher
{
    public async Task<int> DispatchAsync(int limit,CancellationToken ct)
    {
        var processed=0;
        for(var index=0;index<Math.Clamp(limit,1,20);index++)
        {
            await using var connection=await factory.OpenAsync(ct);
            var now=clock.UtcNow;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE transactional_email_outbox SET state='dead_lettered',payload=NULL,last_error='ExpiredOrExhausted',locked_until=NULL,lease_id=NULL
                WHERE (state IN ('pending','sending') AND expires_at<=@now)
                   OR (attempts>=6 AND (state='pending' OR (state='sending' AND locked_until<=@now)))
                """,new{now},cancellationToken:ct));
            var lease=Guid.NewGuid().ToString("N");
            MailRow? row;
            await using(var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,ct))
            {
                row=await connection.QuerySingleOrDefaultAsync<MailRow>(new CommandDefinition("""
                    SELECT id,payload,attempts FROM transactional_email_outbox
                    WHERE expires_at>@now AND attempts<6 AND ((state='pending' AND next_attempt_at<=@now) OR (state='sending' AND locked_until<=@now))
                    ORDER BY next_attempt_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                    """,new{now},tx,cancellationToken:ct));
                if(row is null)break;
                await connection.ExecuteAsync(new CommandDefinition("UPDATE transactional_email_outbox SET state='sending',attempts=attempts+1,lease_id=@lease,locked_until=@until WHERE id=@id",new{row.id,lease,until=now.AddMinutes(2)},tx,cancellationToken:ct));
                await tx.CommitAsync(ct);
            }
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));
                await sender.SendAsync(row.id,protector.Unprotect(row.payload),timeout.Token);
                await connection.ExecuteAsync(new CommandDefinition("UPDATE transactional_email_outbox SET state='sent',payload=NULL,completed_at=@now,locked_until=NULL,lease_id=NULL,last_error=NULL WHERE id=@id AND lease_id=@lease AND state='sending'",new{row.id,lease,now=clock.UtcNow},cancellationToken:ct));
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE transactional_email_outbox SET state=CASE WHEN attempts>=6 THEN 'dead_lettered' ELSE 'pending' END,
                        payload=CASE WHEN attempts>=6 THEN NULL ELSE payload END,next_attempt_at=@next,locked_until=NULL,lease_id=NULL,last_error=@error
                    WHERE id=@id AND lease_id=@lease AND state='sending'
                    """,new{row.id,lease,next=clock.UtcNow.AddMinutes(Math.Pow(2,row.attempts)),error=ex.GetType().Name},cancellationToken:ct));
            }
            processed++;
        }
        return processed;
    }
    private sealed class MailRow {public string id {get;set;}="";public byte[] payload {get;set;}=[];public int attempts {get;set;}}
}
