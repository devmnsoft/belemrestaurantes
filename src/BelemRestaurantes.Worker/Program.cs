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
        using var host = Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseWindowsService(options => options.ServiceName = "BelemRestauranteCuiaDiscovery")
            .ConfigureServices((hostContext, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(hostContext.Configuration);
                services.AddHostedService<ConfigurationDiagnosticsService>();
                services.AddHostedService<MigrationHostedService>();
                services.AddHostedService<DiscoveryHostedService>();
                services.AddHostedService<EmailHostedService>();
                services.AddHostedService<CrawlerHostedService>();
            })
            .ConfigureLogging(logging => logging.AddConsole())
            .Build();
        if (args.Contains("--verify-discovery", StringComparer.Ordinal))
        {
            DiscoveryEvidence.PrintAsync(host.Services).GetAwaiter().GetResult();
            return;
        }
        host.Run();
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
        if (!configuration.GetValue<bool>("Database:ApplyMigrations", false))
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
