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

        var every = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue<int>("Discovery:IntervalMinutes", 10)));
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
        logger.LogInformation("Discovery: início de ciclo às {StartedAt}", startedAt);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IDiscoveryQueue>();
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
            var processedJobs = 0;

            var metricsTotal = new AggregateMetrics();

            for (var index = 0; index < batchSize; index++)
            {
                var claimed = await queue.ClaimDueAsync(1, InstanceId, TimeSpan.FromMinutes(4), ct);
                if (claimed.Count == 0) break;
                var job = claimed[0];
                processedJobs++;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    var discovered = await ProcessOneAsync(scope, job, timeout.Token);
                    metricsTotal.Add(discovered);
                    await teleportory.RecordTargetAsync(cycleId, job, discovered, ct);
                    if (discovered.JobsFailed > 0) throw new InvalidOperationException("O processamento teve falhas; reagendado para nova tentativa.");

                    await queue.CompleteAsync(job.JobId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Job {JobId} ({City}/{Category}) falhou.", job.JobId, job.City, job.Category);

                    await queue.FailAsync(job.JobId, ex.Message, TimeSpan.FromMinutes(Math.Pow(2, Math.Min(job.Attempt, 6))), maxAttempts, ct);
                }
            }

            var duration = DateTimeOffset.UtcNow - startedAt;
            if (processedJobs == 0)
                logger.LogInformation("Discovery sem tarefas elegíveis: verifique alvos habilitados, próxima execução e jobs em retry/dead-letter.");

            await teleportory.CompleteCycleAsync(
                cycleId,
                processedJobs,
                metricsTotal.Discovered,
                metricsTotal.Created,
                metricsTotal.Updated,
                metricsTotal.Failed,
                metricsTotal.Skipped,
                duration,
                ct);

            logger.LogInformation(
                "Ciclo concluído: lote={Jobs} {Metrics} tempo={Duration:F1}s",
                processedJobs, metricsTotal.ToString(), duration.TotalSeconds);
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
        await using var db=await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(ct);
        var target=await Dapper.SqlMapper.QuerySingleAsync(db,new Dapper.CommandDefinition("SELECT provider,result_limit FROM discovery_targets WHERE id=@id",new{id=job.TargetId},cancellationToken:ct));
        provider=(string)target.provider switch {
            "openstreetmap"=>scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.OpenData.OpenStreetMapDiscoveryClient>(),
            "googleplaces"=>scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Google.GooglePlacesClient>(),
            _=>provider
        };
        provider=new LimitedProvider(provider,(int)target.result_limit);
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

    private sealed class LimitedProvider(IPlaceDiscoveryProvider inner,int limit):IPlaceDiscoveryProvider
    {
        public string Name=>inner.Name;
        public bool IsConfigured=>inner.IsConfigured;
        public async Task<BelemRestaurantes.Application.Models.PlaceSearchResult> SearchAsync(string query,string? city,CancellationToken ct)
        {var result=await inner.SearchAsync(query,city,ct);return result with{Places=result.Places.Take(limit).ToArray()};}
        public Task<BelemRestaurantes.Application.Models.DiscoveredPlace?> GetDetailsAsync(string externalId,CancellationToken ct)=>inner.GetDetailsAsync(externalId,ct);
    }
}
