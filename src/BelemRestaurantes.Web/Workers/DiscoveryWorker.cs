using BelemRestaurantes.Web.Services;
namespace BelemRestaurantes.Web.Workers;
public sealed class DiscoveryWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<DiscoveryWorker> log):BackgroundService {
 protected override async Task ExecuteAsync(CancellationToken ct) {
  if(!config.GetValue<bool>("Discovery:Enabled")) {log.LogInformation("Discovery disabled until explicitly enabled");return;}
  var every=TimeSpan.FromMinutes(Math.Max(10,config.GetValue("Discovery:IntervalMinutes",10)));
  using var timer=new PeriodicTimer(every);
  do {
   try {
    using var scope=scopes.CreateScope();
    var queue=scope.ServiceProvider.GetRequiredService<DiscoveryQueue>();
    var sync=scope.ServiceProvider.GetRequiredService<ProviderSync>();
    var targets=await queue.AcquireBatch(config.GetValue("Discovery:BatchSize",5),ct);
    foreach(var t in targets) {
     try {await sync.DiscoverTarget(t.City,t.Category,ct);await queue.Finish(t.Id,true,null,ct);}
     catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
     catch(Exception ex){log.LogError(ex,"Discovery failed for {City}/{Category}",t.City,t.Category);await queue.Finish(t.Id,false,ex.GetType().Name,ct);}
    }
   } catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
     catch(Exception ex){log.LogError(ex,"Discovery cycle failed; verify SQL migrations and provider credentials");}
   try {if(!await timer.WaitForNextTickAsync(ct))break;}catch(OperationCanceledException){break;}
  }while(!ct.IsCancellationRequested);
 }
}