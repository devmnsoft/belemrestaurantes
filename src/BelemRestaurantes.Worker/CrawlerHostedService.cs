using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>
/// Cuia Crawler como serviço autônomo em background no Worker/Windows Service.
/// Executa ciclos a cada 10 minutos para pesquisa e extração contínua e gentil de fontes públicas.
/// </summary>
public sealed class CrawlerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CrawlerHostedService> _logger;
    private static readonly string InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

    public CrawlerHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CrawlerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("Crawler:Enabled"))
        {
            _logger.LogInformation("Cuia Crawler desativado no ambiente atual (Crawler:Enabled=false).");
            return;
        }

        var intervalMinutes = Math.Max(5, _configuration.GetValue<int>("Crawler:IntervalMinutes", 10));
        var every = TimeSpan.FromMinutes(intervalMinutes);
        var maxTasks = Math.Clamp(_configuration.GetValue<int>("Crawler:MaxTasksPerCycle", 10), 1, 30);

        _logger.LogInformation(
            "Cuia Crawler iniciado: intervalo de {Interval}min, max {MaxTasks} tarefas/ciclo, instância {Instance}",
            every.TotalMinutes, maxTasks, InstanceId);

        using var timer = new PeriodicTimer(every);

        // Executa uma primeira rodada inicial após 30 segundos de inicialização para validação do serviço
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            if (!stoppingToken.IsCancellationRequested)
            {
                await RunSingleCycleAsync(maxTasks, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await RunSingleCycleAsync(maxTasks, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no ciclo do Cuia Crawler. O próximo ciclo segue normalmente.");
            }
        }

        _logger.LogInformation("Cuia Crawler finalizado.");
    }

    private async Task RunSingleCycleAsync(int maxTasks, CancellationToken ct)
    {
        _logger.LogInformation("Iniciando ciclo autônomo do Cuia Crawler...");

        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrawlerOrchestrator>();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(8));

            var processed = await orchestrator.RunCrawlerCycleAsync(maxTasks, timeoutCts.Token);
            _logger.LogInformation("Ciclo do Cuia Crawler concluído com sucesso: {Count} tarefas processadas.", processed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Saída normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha durante execução do ciclo do Cuia Crawler.");
        }
    }
}
