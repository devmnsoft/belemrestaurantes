using Dapper;
using BelemRestaurantes.Web.Data;
namespace BelemRestaurantes.Web.Services;
public sealed record DiscoveryTarget(long Id,string City,string Category);
public sealed class DiscoveryQueue(DbFactory db) {
 public async Task<IReadOnlyList<DiscoveryTarget>> AcquireBatch(int batchSize,CancellationToken token) {
  await using var c=db.Open();
  var rows=await c.QueryAsync<DiscoveryTarget>(new CommandDefinition(@"WITH acquired AS (
SELECT id FROM public.discovery_targets
WHERE enabled=true AND next_run_at<=now() AND (locked_until IS NULL OR locked_until<now())
ORDER BY next_run_at,id LIMIT @n FOR UPDATE SKIP LOCKED)
UPDATE public.discovery_targets d
SET locked_until=now()+interval '4 minutes',attempts=d.attempts+1
FROM acquired WHERE d.id=acquired.id
RETURNING d.id,d.city,d.category",new{n=Math.Clamp(batchSize,1,15)},cancellationToken:token));
  return rows.ToArray();
 }
 public async Task Finish(long id,bool success,string? error,CancellationToken token) {
  await using var c=db.Open();
  await c.ExecuteAsync(new CommandDefinition(@"UPDATE public.discovery_targets SET
locked_until=NULL,
next_run_at=now()+(CASE WHEN @success THEN interval '12 hours' ELSE interval '1 hour' END),
last_success_at=CASE WHEN @success THEN now() ELSE last_success_at END,
last_error=CASE WHEN @success THEN NULL ELSE left(@error,500) END
WHERE id=@id",new{id,success,error},cancellationToken:token));
 }
}