using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace BelemRestaurantes.Worker;
public sealed class MaintenanceHostedService(IServiceProvider services,ILogger<MaintenanceHostedService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try{using var scope=services.CreateScope();if(await scope.ServiceProvider.GetRequiredService<MySqlMaintenanceJobs>().RunNextAsync(stoppingToken))logger.LogInformation("Rotina de validade de horários/fontes processada; confira o resultado persistido na operação.");}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception ex){logger.LogError(ex,"Falha na verificação operacional; nova tentativa no próximo ciclo.");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
