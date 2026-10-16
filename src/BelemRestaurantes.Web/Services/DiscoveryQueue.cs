using Dapper;
using BelemRestaurantes.Web.Data;
namespace BelemRestaurantes.Web.Services;
public sealed record DiscoveryTarget(long Id,string City,string Category);
public sealed class DiscoveryQueue(DbFactory db){
 public async Task<IReadOnlyList<DiscoveryTarget>> AcquireBatch(int batchSize,CancellationToken token){
  await using var c=db.Open();
  await c.OpenAsync(token);
  await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,token);
  var rows=(await c.QueryAsync<DiscoveryTarget>(new CommandDefinition(@"SELECT id,city,category FROM discovery_targets WHERE enabled=TRUE AND next_run_at<=UTC_TIMESTAMP(6) AND (locked_until IS NULL OR locked_until<UTC_TIMESTAMP(6)) ORDER BY next_run_at,id LIMIT @n FOR UPDATE SKIP LOCKED",new{n=Math.Clamp(batchSize,1,15)},transaction:tx,cancellationToken:token))).ToArray();
  foreach(var item in rows) await c.ExecuteAsync(new CommandDefinition(@"UPDATE discovery_targets SET locked_until=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 4 MINUTE),attempts=attempts+1 WHERE id=@id",new{id=item.Id},transaction:tx,cancellationToken:token));
  await tx.CommitAsync(token);
  return rows;
 }
 public async Task Finish(long id,bool success,string? error,CancellationToken token){
  await using var c=db.Open();
  await c.ExecuteAsync(new CommandDefinition(@"UPDATE discovery_targets SET locked_until=NULL,next_run_at=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL IF(@success,12,1) HOUR),last_success_at=IF(@success,UTC_TIMESTAMP(6),last_success_at),last_error=IF(@success,NULL,LEFT(@error,500)) WHERE id=@id",new{id,success,error},cancellationToken:token));
 }
}