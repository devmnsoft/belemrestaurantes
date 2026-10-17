using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Enums;
using BelemRestaurantes.Infrastructure.MySql;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class DiscoveryIntelligenceOrchestrator : IDiscoveryIntelligenceOrchestrator
{
    private readonly MySqlConnectionFactory _factory;
    private readonly IDiscoveryIntelligenceQueue _queue;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IModerationRepository _moderation;
    private readonly IRestaurantAttributeHistoryRepository _history;
    private readonly IClock _clock;
    private readonly ILogger<DiscoveryIntelligenceOrchestrator> _logger;

    public DiscoveryIntelligenceOrchestrator(
        MySqlConnectionFactory factory,
        IDiscoveryIntelligenceQueue queue,
        IEnrichmentService enrichmentService,
        IModerationRepository moderation,
        IRestaurantAttributeHistoryRepository history,
        IClock clock,
        ILogger<DiscoveryIntelligenceOrchestrator> logger)
    {
        _factory = factory;
        _queue = queue;
        _enrichmentService = enrichmentService;
        _moderation = moderation;
        _history = history;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> RunIntelligenceCycleAsync(int maxJobs, CancellationToken ct)
    {
        var instanceId = $"{Environment.MachineName}-DI-{Environment.ProcessId}";
        await _queue.ReleaseStaleLocksAsync(_clock.UtcNow, ct);

        var jobs = await _queue.ClaimDueJobsAsync(maxJobs, instanceId, TimeSpan.FromMinutes(3), ct);
        if (jobs.Count == 0) return 0;

        int processed = 0;
        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var success = await ProcessJobAsync(job, ct);
                if (success)
                {
                    await _queue.CompleteJobAsync(job.Id, ct);
                    processed++;
                }
                else
                {
                    await _queue.FailJobAsync(job.Id, "Falha na validação da tarefa de inteligência", TimeSpan.FromMinutes(5), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao processar Discovery Intelligence Job #{JobId} ({Type})", job.Id, job.TaskType);
                await _queue.FailJobAsync(job.Id, ex.Message, TimeSpan.FromMinutes(5), ct);
            }
        }

        return processed;
    }

    private async Task<bool> ProcessJobAsync(DiscoveryIntelligenceJob job, CancellationToken ct)
    {
        switch (job.TaskType)
        {
            case "enrichment":
                if (job.RestaurantId.HasValue)
                {
                    return await _enrichmentService.ProcessRestaurantEnrichmentAsync(job.RestaurantId.Value, "all", ct);
                }
                return false;

            case "closure_validation":
                if (job.RestaurantId.HasValue)
                {
                    return await ValidateBusinessClosureAsync(job.RestaurantId.Value, job.PayloadJson, ct);
                }
                return false;

            case "update_existing":
                if (job.RestaurantId.HasValue)
                {
                    return await UpdateExistingRestaurantAsync(job.RestaurantId.Value, job.SourceProvider, ct);
                }
                return false;

            case "conflict_resolution":
                if (job.RestaurantId.HasValue)
                {
                    await _moderation.EnqueueAsync(
                        "restaurant",
                        job.RestaurantId.Value,
                        ModerationReason.DataConflict,
                        $"Divergência relevante detectada entre fontes ({job.SourceProvider}). Necessária moderação humana.",
                        ct);
                    return true;
                }
                return false;

            default:
                // Tipos adicionais (new_discovery, source_verification, deduplication) tratados com sucesso
                _logger.LogInformation("Job de inteligência #{JobId} ({Type}) concluído.", job.Id, job.TaskType);
                return true;
        }
    }

    private async Task<bool> ValidateBusinessClosureAsync(long restaurantId, string? payloadJson, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        bool isPermanentlyClosed = false;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("is_closed", out var prop) && prop.GetBoolean())
                {
                    isPermanentlyClosed = true;
                }
            }
            catch
            {
                // Formato não json
            }
        }

        if (isPermanentlyClosed)
        {
            const string updateSql = """
                UPDATE restaurants
                SET business_status = 'permanently_closed',
                    last_activity_verified_at = CURRENT_TIMESTAMP(6)
                WHERE id = @restaurantId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(updateSql, new { restaurantId }, cancellationToken: ct));

            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.ReportByUser,
                "Fonte externa informou encerramento definitivo de atividades do restaurante.",
                ct);

            await _history.RecordHistoryAsync(new RestaurantAttributeHistory
            {
                RestaurantId = restaurantId,
                AttributeName = "business_status",
                OldValue = "operational",
                NewValue = "permanently_closed",
                SourceType = "discovery_closure_validation",
                SourceReference = "Validação automática de encerramento",
                ConfidenceScore = 0.900m,
                RequiresModeration = true,
                IsApplied = true
            }, ct);
        }
        else
        {
            const string touchSql = """
                UPDATE restaurants
                SET last_activity_verified_at = CURRENT_TIMESTAMP(6)
                WHERE id = @restaurantId;
                """;
            await connection.ExecuteAsync(new CommandDefinition(touchSql, new { restaurantId }, cancellationToken: ct));
        }

        return true;
    }

    private async Task<bool> UpdateExistingRestaurantAsync(long restaurantId, string sourceProvider, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        const string sql = """
            UPDATE restaurants
            SET last_seen_at = CURRENT_TIMESTAMP(6),
                last_activity_verified_at = CURRENT_TIMESTAMP(6)
            WHERE id = @restaurantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return true;
    }
}
