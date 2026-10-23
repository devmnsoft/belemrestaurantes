using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>
/// Cuia Crawler como serviço autônomo em background no Worker/Windows Service.
/// Executa ciclos a cada minuto para pesquisa e extração contínua e gentil de fontes públicas.
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

        var externalEvery = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue<int>("Crawler:IntervalMinutes", 1)));
        var queueEvery = WorkerCadence.QueueCheck(_configuration.GetValue<int?>("Crawler:QueueCheckMinutes"));
        var maxTasks = Math.Clamp(_configuration.GetValue<int>("Crawler:MaxTasksPerCycle", 10), 1, 30);

        _logger.LogInformation(
            "Cuia Crawler iniciado: fila a cada {Queue}min, fonte externa a cada {Interval}min, max {MaxTasks} tarefas/ciclo, instância {Instance}",
            queueEvery.TotalMinutes, externalEvery.TotalMinutes, maxTasks, InstanceId);

        using var timer = new PeriodicTimer(queueEvery);
        DateTimeOffset? lastExternal = null;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (WorkerCadence.ExternalDue(DateTimeOffset.UtcNow, lastExternal, externalEvery))
                {
                    await RunSingleCycleAsync(maxTasks, stoppingToken);
                    lastExternal = DateTimeOffset.UtcNow;
                }
                else
                {
                    await MaintainQueueAsync(stoppingToken);
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
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

    private async Task MaintainQueueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrawlerOrchestrator>();
        await orchestrator.MaintainQueueAsync(ct);
        var enrichment = scope.ServiceProvider.GetService<IEnrichmentService>();
        if (enrichment is not null)
        {
            var queued = await enrichment.EnqueueIncompleteProfilesAsync(20, ct);
            if (queued > 0) _logger.LogInformation("Fila de fichas incompletas: {Count} novos itens, sem consulta externa neste ciclo.", queued);
        }
        _logger.LogDebug("Fila do rastreador conferida sem baixar páginas.");
    }

    private async Task RunSingleCycleAsync(int maxTasks, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Iniciando ciclo autônomo do Cuia Crawler...");

        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrawlerOrchestrator>();
        var telemetryRepo = scope.ServiceProvider.GetService<IDiscoveryTelemetryRepository>();

        int crawlerProcessed = 0;
        int enrichedCount = 0;
        int intelJobs = 0;
        string status = "success";
        string? errorMessage = null;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(8));

            crawlerProcessed = await orchestrator.RunCrawlerCycleAsync(maxTasks, timeoutCts.Token);
            _logger.LogInformation("Ciclo do Cuia Crawler concluído com sucesso: {Count} tarefas processadas.", crawlerProcessed);

            // Sprint 09: Enriquecimento inteligente contínuo de estabelecimentos (Fase 3)
            var enrichmentService = scope.ServiceProvider.GetService<IEnrichmentService>();
            if (enrichmentService != null)
            {
                var queuedPhotos = await enrichmentService.EnqueueLicensedPhotoSweepAsync(15, timeoutCts.Token);
                if (queuedPhotos > 0) _logger.LogInformation("Fila de fotos licenciadas: {Count} estabelecimentos publicados sem foto.", queuedPhotos);
                enrichedCount = await enrichmentService.RunEnrichmentCycleAsync(10, timeoutCts.Token);
                if (enrichedCount > 0)
                {
                    _logger.LogInformation("Ciclo de Enriquecimento Discovery 360: {Count} estabelecimentos enriquecidos.", enrichedCount);
                }
            }

            // Sprint 10: Fila central unificada do Cuia Discovery Intelligence (Fase 2)
            var intelligenceOrchestrator = scope.ServiceProvider.GetService<IDiscoveryIntelligenceOrchestrator>();
            if (intelligenceOrchestrator != null)
            {
                intelJobs = await intelligenceOrchestrator.RunIntelligenceCycleAsync(10, timeoutCts.Token);
                if (intelJobs > 0)
                {
                    _logger.LogInformation("Ciclo Discovery Intelligence: {Count} tarefas de inteligência processadas.", intelJobs);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = "warning";
            errorMessage = "Ciclo interrompido por cancelamento ou parada do serviço.";
        }
        catch (Exception ex)
        {
            status = "error";
            errorMessage = ex.Message;
            _logger.LogError(ex, "Falha durante execução do ciclo do Cuia Crawler.");
        }
        finally
        {
            var finishedAt = DateTimeOffset.UtcNow;
            if (telemetryRepo != null)
            {
                try
                {
                    await telemetryRepo.RecordTelemetryAsync(
                        serviceName: "Crawler & Intelligence (tarefas)",
                        startedAt: startedAt,
                        finishedAt: finishedAt,
                        sourceQueried: "Sitemaps, Feeds & Schema.org",
                        // Tarefas processadas não são restaurantes criados, nem unidades de API.
                        // Os números de ingestão reais estão em crawler_runs.
                        recordsCreated: 0,
                        recordsUpdated: enrichedCount,
                        apiUnitsConsumed: 0,
                        status: status,
                        errorMessage: errorMessage,
                        ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao registrar telemetria do ciclo no banco de dados.");
                }
            }
        }
    }
}
