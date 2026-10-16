using BelemRestaurantes.Web.Services;
namespace BelemRestaurantes.Web.Workers;
public sealed class DiscoveryWorker(IServiceProvider provider,IConfiguration config,ILogger<DiscoveryWorker> log):BackgroundService {
 protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
  if(!config.GetValue<bool>("Discovery:Enabled")) {log.LogInformation("Discovery disabled until configured");return;}
  var cities=config.GetSection("Discovery:Cities").Get<string[]>()??[];
  var interval=TimeSpan.FromMinutes(Math.Max(10,config.GetValue("Discovery:IntervalMinutes",10)));
  do {
   foreach(var city in cities) {
    if(stoppingToken.IsCancellationRequested)break;
    using var scope=provider.CreateScope();
    var sync=scope.ServiceProvider.GetRequiredService<ProviderSync>();
    await sync.DiscoverCity(city,stoppingToken);
   }
   try {await Task.Delay(interval,stoppingToken);} catch(OperationCanceledException){break;}
  } while(!stoppingToken.IsCancellationRequested);
 }
}