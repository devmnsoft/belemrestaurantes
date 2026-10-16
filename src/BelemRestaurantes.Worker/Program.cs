using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Application;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = "BelemRestauranteCuiaDiscovery")
            .ConfigureServices((hostContext, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(hostContext.Configuration);
                services.AddHostedService<MigrationHostedService>();
                services.AddHostedService<DiscoveryHostedService>();
            })
            .ConfigureLogging(logging => logging.AddConsole())
            .Build()
            .Run();
    }
}

/// <summary>
/// Aplica as migrações embutidas antes do ciclo de descoberta. Fonte única de
/// schema: os scripts em Infrastructure/Migrations/Scripts. Sem banco
/// disponível o serviço para — descoberta sem schema é incidente, não degradação.
/// </summary>
public sealed class MigrationHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:ApplyMigrations", true))
        {
            logger.LogInformation("Aplicação de migrações desativada (Database:ApplyMigrations=false).");
            return;
        }

        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        var result = await runner.ApplyAsync(cancellationToken);

        logger.LogInformation(
            "Migrações verificadas: {Applied} aplicadas, {Current} já em vigor.",
            result.Applied, result.AlreadyCurrent.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}