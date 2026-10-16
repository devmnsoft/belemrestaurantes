using BelemRestaurantes.Application;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>
/// Cuia Discovery como host contínuo. Cada ciclo:
/// 1. Libera jobs travados além do prazo (recuperação após crash).
/// 2. Coloca alvos vencidos na fila persistente.
/// 3. Reserva jobs (SELECT ... FOR UPDATE SKIP LOCKED) — sem duplicidade entre instâncias.
/// 4. Executa com política de retry/backoff/circuit breaker.
/// 5. Registra métricas do ciclo (descobertas, atualizações, falhas e tempo).
/// </summary>
public sealed class DiscoveryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DiscoveryHostedService> logger) : BackgroundService
{
    private static readonly string InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Discovery:Enabled"))
        {
            logger.LogInformation("Discovery disabled until explicitly enabled (Discovery:Enabled=true)");
            return;
        }

        var every = TimeSpan.FromMinutes(Math.Max(10, configuration.GetValue<int>("Discovery:IntervalMinutes", 10)));
        var batchSize = Math.Clamp(configuration.GetValue<int>("Discovery:BatchSize", 5), 1, 50);
        var maxAttempts = 3;

        logger.LogInformation(
            "Cuia Discovery iniciado: intervalo {Interval}min, lote {BatchSize}, instância {Instance}",
            every.TotalMinutes, batchSize, InstanceId);

        using var timer = new PeriodicTimer(every);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(batchSize, maxAttempts, stoppingToken);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Cuia Discovery finalizado.");
    }

    private async Task RunCycleAsync(int batchSize, int maxAttempts, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IDiscoveryQueue>();
            var targets = scope.ServiceProvider.GetRequiredService<IDiscoveryTargetRepository>();
            var orchestratorFactory = scope.ServiceProvider.GetRequiredService<IServiceProvider>();
            var teleportory = scope.ServiceProvider.GetRequiredService<ICycleTelemetryRepository>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var cycleId = await teleportory.StartCycleAsync(InstanceId, startedAt, ct);

            // 1. Recuperar travados além do prazo.
            var released = await queue.ReleaseStaleLocksAsync(clock.UtcNow, ct);
            if (released > 0)
            {
                logger.LogWarning("Liberados {Count} jobs travados além do prazo.", released);
            }

            // 2. Colocar alvos vencidos na fila.
            var enqueued = await queue.EnqueueDueTargetsAsync(clock.UtcNow, ct);
            if (enqueued > 0)
            {
                logger.LogInformation("Enfileirados {Count} jobs para alvos vencidos.", enqueued);
            }

            // 3. Reservar lote.
            var jobs = await queue.ClaimDueAsync(batchSize, InstanceId, TimeSpan.FromMinutes(4), ct);

            var metricsTotal = new AggregateMetrics();

            foreach (var job in jobs)
            {
                try
                {
                    var discovered = await ProcessOneAsync(scope, job, ct);
                    metricsTotal.Add(discovered);

                    await queue.CompleteAsync(job.JobId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Job {JobId} ({City}/{Category}) falhou.", job.JobId, job.City, job.Category);

                    await queue.FailAsync(job.JobId, ex.Message, TimeSpan.FromMinutes(1 * (job.Attempt + 1)), maxAttempts, ct);
                }
            }

            var duration = DateTimeOffset.UtcNow - startedAt;

            await teleportory.CompleteCycleAsync(
                cycleId,
                jobs.Count,
                metricsTotal.Discovered,
                metricsTotal.Created,
                metricsTotal.Updated,
                metricsTotal.Failed,
                metricsTotal.Skipped,
                duration,
                ct);

            logger.LogInformation(
                "Ciclo concluído: lote={Jobs} {Metrics} tempo={Duration:F1}s",
                jobs.Count, metricsTotal.ToString(), duration.TotalSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Saída ordenada.
        }
        catch (Exception ex)
        {
            // Falha global do ciclo não pode impedir o próximo ciclo.
            logger.LogError(ex, "Ciclo de descoberta falhou; verificar credenciais SQL e APIs. O próximo ciclo segue normalmente.");
        }
    }

    private async Task<DiscoveryCycleMetrics> ProcessOneAsync(IServiceScope scope, DiscoveryWorkItem job, CancellationToken ct)
    {
        var provider = scope.ServiceProvider.GetRequiredService<IPlaceDiscoveryProvider>();
        var ingestion = scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>();
        var moderation = scope.ServiceProvider.GetRequiredService<IModerationRepository>();
        var router = scope.ServiceProvider.GetRequiredService<IAiRouter>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var orchestrator = new DiscoveryOrchestrator(provider, ingestion, moderation, router, clock,
            scope.ServiceProvider.GetRequiredService<ILogger<DiscoveryOrchestrator>>());

        return await orchestrator.RunTargetAsync(job, ct);
    }

    private sealed class AggregateMetrics
    {
        public int Discovered { get; private set; }
        public int Created { get; private set; }
        public int Updated { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }

        public void Add(DiscoveryCycleMetrics m)
        {
            Discovered += m.PlacesDiscovered;
            Created += m.RestaurantsCreated;
            Updated += m.RestaurantsUpdated;
            Failed += m.JobsFailed;
            Skipped += m.PlacesSkipped;
        }

        public override string ToString() =>
            $"descobertos={Discovered} novos={Created} atualizados={Updated} ignorados={Skipped} falhas={Failed}";
    }
}