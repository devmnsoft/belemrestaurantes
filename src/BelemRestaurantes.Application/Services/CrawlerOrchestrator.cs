using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Enums;
using BelemRestaurantes.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Application.Services;

public sealed class CrawlerOrchestrator : ICrawlerOrchestrator
{
    private readonly ICrawlerSourceRepository _sources;
    private readonly ICrawlerTaskQueue _taskQueue;
    private readonly ICrawlerTelemetryRepository _telemetry;
    private readonly ISafeHttpClient _httpClient;
    private readonly IRobotsTxtService _robotsTxtService;
    private readonly ICrawlerParser _parser;
    private readonly IRestaurantIngestionRepository _ingestion;
    private readonly IModerationRepository _moderation;
    private readonly IAiRouter _aiRouter;
    private readonly IEnrichmentQueueRepository? _enrichmentQueue;
    private readonly IRestaurantMediaRepository? _mediaRepo;
    private readonly IRestaurantAttributeHistoryRepository? _historyRepo;
    private readonly IGeoMunicipalityRepository? _geoRepo;
    private readonly IClock _clock;
    private readonly ILogger<CrawlerOrchestrator> _logger;

    public CrawlerOrchestrator(
        ICrawlerSourceRepository sources,
        ICrawlerTaskQueue taskQueue,
        ICrawlerTelemetryRepository telemetry,
        ISafeHttpClient httpClient,
        IRobotsTxtService robotsTxtService,
        ICrawlerParser parser,
        IRestaurantIngestionRepository ingestion,
        IModerationRepository moderation,
        IAiRouter aiRouter,
        IClock clock,
        ILogger<CrawlerOrchestrator> logger,
        IEnrichmentQueueRepository? enrichmentQueue = null,
        IRestaurantMediaRepository? mediaRepo = null,
        IRestaurantAttributeHistoryRepository? historyRepo = null,
        IGeoMunicipalityRepository? geoRepo = null)
    {
        _sources = sources;
        _taskQueue = taskQueue;
        _telemetry = telemetry;
        _httpClient = httpClient;
        _robotsTxtService = robotsTxtService;
        _parser = parser;
        _ingestion = ingestion;
        _moderation = moderation;
        _aiRouter = aiRouter;
        _clock = clock;
        _logger = logger;
        _enrichmentQueue = enrichmentQueue;
        _mediaRepo = mediaRepo;
        _historyRepo = historyRepo;
        _geoRepo = geoRepo;
    }

    public async Task<int> RunCrawlerCycleAsync(int maxTasks, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

        // 1. Recuperar locks travados
        await _taskQueue.ReleaseStaleLocksAsync(now, ct);

        // 2. Verificar fontes ativas e sincronizar sitemaps/feeds para alimentar a fila
        await SeedTasksFromSourcesAsync(ct);

        // 3. Reservar tarefas pendentes
        var batch = await _taskQueue.ClaimDueTasksAsync(Math.Clamp(maxTasks, 1, 20), instanceId, TimeSpan.FromMinutes(3), ct);
        if (batch.Count == 0)
        {
            _logger.LogInformation("Nenhuma tarefa de rastreamento pendente elegível no momento.");
            return 0;
        }

        var processedCount = 0;

        foreach (var task in batch)
        {
            if (ct.IsCancellationRequested) break;

            var sw = Stopwatch.StartNew();
            int discoveredInTask = 0;
            int ingestedInTask = 0;
            string? errorMessage = null;
            int statusCode = 0;

            try
            {
                // A. Respeito ao robots.txt
                var (isAllowed, crawlDelay) = await _robotsTxtService.CheckAccessAsync(task.Url, "CuiaCrawler", ct);
                if (!isAllowed)
                {
                    await _taskQueue.FailTaskAsync(task.Id, "Acesso restrito por robots.txt do domínio", TimeSpan.FromDays(7), 1, ct);
                    await _telemetry.RecordRunAsync(new CrawlerRunLog
                    {
                        SourceId = task.SourceId,
                        Url = task.Url,
                        StatusCode = 403,
                        DurationMs = (int)sw.ElapsedMilliseconds,
                        ErrorMessage = "Bloqueado por diretiva robots.txt"
                    }, ct);
                    continue;
                }

                // Cortesia: aguardar Crawl-delay se especificado (máximo 10s para não prender o worker)
                if (crawlDelay > 0 && crawlDelay <= 10)
                {
                    await Task.Delay(TimeSpan.FromSeconds(crawlDelay), ct);
                }

                // B. Download seguro com anti-SSRF
                var download = await _httpClient.DownloadAsync(task.Url, timeoutSeconds: 15, ct: ct);
                statusCode = download.StatusCode;

                if (!download.Success || string.IsNullOrWhiteSpace(download.ContentText))
                {
                    errorMessage = download.ErrorMessage ?? $"HTTP {download.StatusCode}";
                    await _taskQueue.FailTaskAsync(task.Id, errorMessage, TimeSpan.FromMinutes(Math.Pow(2, Math.Min(task.Attempts, 5))), 4, ct);
                }
                else
                {
                    // C. Extração de estabelecimentos (Schema.org / Heurística)
                    var restaurants = _parser.ExtractRestaurants(download.ContentText, task.Url, task.City);
                    discoveredInTask = restaurants.Count;

                    foreach (var extracted in restaurants)
                    {
                        var ingested = await ProcessExtractedRestaurantAsync(extracted, task, ct);
                        if (ingested) ingestedInTask++;
                    }

                    // Se a página for um diretório HTML, extrair links de sitemap ou páginas filhas
                    if (task.Depth < 2 && download.ContentType != null && download.ContentType.Contains("html"))
                    {
                        // Enfileirar no máximo 10 URLs filhas do mesmo domínio
                        EnqueueChildLinks(task, download.ContentText);
                    }

                    await _taskQueue.CompleteTaskAsync(task.Id, ct);
                    await _sources.RecordCrawlResultAsync(task.SourceId, true, null, download.ETag, download.LastModified, ct);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogWarning(ex, "Falha ao processar tarefa {TaskId} para {Url}", task.Id, task.Url);
                await _taskQueue.FailTaskAsync(task.Id, ex.Message, TimeSpan.FromMinutes(5), 3, ct);
            }
            finally
            {
                sw.Stop();
                await _telemetry.RecordRunAsync(new CrawlerRunLog
                {
                    SourceId = task.SourceId,
                    Url = task.Url,
                    StatusCode = statusCode,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    ItemsDiscovered = discoveredInTask,
                    ItemsIngested = ingestedInTask,
                    ErrorMessage = errorMessage
                }, ct);

                processedCount++;
            }
        }

        return processedCount;
    }

    private async Task SeedTasksFromSourcesAsync(CancellationToken ct)
    {
        var sources = await _sources.ListSourcesAsync("active", ct);
        foreach (var source in sources)
        {
            // Se a fonte nunca foi rastreada ou já passou de 6 horas
            if (source.LastCrawledAt == null || source.LastCrawledAt < DateTimeOffset.UtcNow.AddHours(-6))
            {
                var download = await _httpClient.DownloadAsync(source.BaseUrl, source.ETag, source.LastModifiedHeader, 15, ct);
                if (download.NotModified)
                {
                    await _sources.RecordCrawlResultAsync(source.Id, true, null, source.ETag, source.LastModifiedHeader, ct);
                    continue;
                }

                if (download.Success && !string.IsNullOrWhiteSpace(download.ContentText))
                {
                    IReadOnlyList<string> urls = [];

                    if (source.SourceType == "sitemap_xml")
                    {
                        urls = _parser.ParseSitemap(download.ContentText, source.BaseUrl);
                    }
                    else if (source.SourceType == "rss_feed")
                    {
                        urls = _parser.ParseRssFeed(download.ContentText, source.BaseUrl).Select(x => x.Url).ToList();
                    }
                    else
                    {
                        urls = [source.BaseUrl];
                    }

                    var tasks = urls.Take(50).Select(u => new CrawlerTask
                    {
                        SourceId = source.Id,
                        Url = u,
                        City = source.TargetCity ?? "Belém",
                        Category = source.DefaultCategory,
                        Depth = 0
                    });

                    await _taskQueue.EnqueueBatchTasksAsync(tasks, ct);
                    await _sources.RecordCrawlResultAsync(source.Id, true, null, download.ETag, download.LastModified, ct);
                }
                else
                {
                    await _sources.RecordCrawlResultAsync(source.Id, false, download.ErrorMessage, null, null, ct);
                }
            }
        }
    }

    private async Task<bool> ProcessExtractedRestaurantAsync(ExtractedRestaurantData extracted, CrawlerTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extracted.Name)) return false;

        var normalizedName = NameNormalizer.Normalize(extracted.Name);
        var city = extracted.City ?? task.City;

        // Deduplicação contra o catálogo existente
        var candidates = await _ingestion.FindDuplicationCandidatesAsync(normalizedName, city, null, ct);
        var externalId = $"crawler:{Math.Abs(extracted.SourceUrl.GetHashCode())}";
        var dedupResult = DeduplicationService.Compare(externalId, extracted.Name, city, candidates);

        var place = new DiscoveredPlace(
            ExternalId: externalId,
            Name: extracted.Name,
            Neighborhood: extracted.Neighborhood,
            City: city,
            Category: extracted.Category ?? extracted.ServesCuisine ?? task.Category ?? "Regional",
            Address: extracted.Address,
            Latitude: extracted.Latitude,
            Longitude: extracted.Longitude,
            GoogleMapsUri: null,
            Website: extracted.Website ?? extracted.SourceUrl,
            GoogleRating: null,
            GoogleRatingCount: null,
            Types: ["restaurant", "food", "crawler_discovered"]
        );

        var restaurantId = await _ingestion.UpsertDiscoveredAsync(
            place,
            provider: $"crawler:{task.SourceId}",
            now: _clock.UtcNow,
            ct: ct);

        // Discovery 360: Registrar histórico inicial do nome e cidade
        if (_historyRepo != null)
        {
            await _historyRepo.RecordHistoryAsync(new RestaurantAttributeHistory
            {
                RestaurantId = restaurantId,
                AttributeName = "name",
                OldValue = null,
                NewValue = extracted.Name,
                SourceType = "crawler_discovery",
                SourceReference = extracted.SourceUrl,
                ConfidenceScore = (decimal)extracted.ConfidenceScore,
                RequiresModeration = false,
                IsApplied = true
            }, ct);
        }

        // Discovery 360: Adicionar foto licenciada externa se extraída
        if (!string.IsNullOrWhiteSpace(extracted.PhotoUrl) && _mediaRepo != null)
        {
            await _mediaRepo.AddMediaAsync(new RestaurantMedia
            {
                RestaurantId = restaurantId,
                MediaType = "photo_dish",
                MediaUrl = extracted.PhotoUrl,
                PriorityTier = (int)MediaPriorityTier.LicensedExternal,
                SourceOrigin = extracted.SourceUrl,
                AttributionText = extracted.PhotoAttribution ?? $"Foto via {extracted.SourceUrl}",
                LicenseType = "licensed_external",
                Status = "pending"
            }, ct);
        }

        // Discovery 360: Enfileirar para enriquecimento contínuo (redes sociais, horário, cardápio)
        if (_enrichmentQueue != null)
        {
            await _enrichmentQueue.EnqueueAsync(restaurantId, priority: 1, fields: "all", ct: ct);
        }

        // Discovery 360: Atualizar telemetria do município se existente
        if (_geoRepo != null)
        {
            var geo = await _geoRepo.GetByNameAsync(city, ct);
            if (geo != null)
            {
                await _geoRepo.UpdateScanTimestampAsync(geo.IbgeCode, _clock.UtcNow, "in_progress", ct);
            }
        }

        // Se a deduplicação indicou incerteza ou conflito, enfileira para moderação humana
        if (dedupResult.RequiresHumanReview || extracted.HasConflict)
        {
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DataConflict,
                $"Fonte autônoma {extracted.SourceUrl} encontrou divergência: {dedupResult.Rationale}",
                ct);
        }
        else if (dedupResult.Outcome == MatchOutcome.ProbableDuplicate)
        {
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DuplicateSuspected,
                $"Suspeita de duplicidade com estabelecimento #{dedupResult.MatchedRestaurantId}: {dedupResult.Rationale}",
                ct);
        }
        else
        {
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.NewDiscovery,
                $"Novo estabelecimento gastronômico descoberto autonomamente via {extracted.SourceUrl} ({city})",
                ct);
        }

        return true;
    }

    private void EnqueueChildLinks(CrawlerTask parentTask, string htmlContent)
    {
        try
        {
            if (!Uri.TryCreate(parentTask.Url, UriKind.Absolute, out var parentUri)) return;

            var matches = System.Text.RegularExpressions.Regex.Matches(htmlContent, @"href=[""'](\/[a-zA-Z0-9_\-\/]+)[""']");
            var childUrls = new List<CrawlerTask>();

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var relative = m.Groups[1].Value;
                if (relative.EndsWith(".css") || relative.EndsWith(".js") || relative.EndsWith(".png") || relative.EndsWith(".jpg")) continue;

                var combined = new Uri(parentUri, relative).ToString();
                childUrls.Add(new CrawlerTask
                {
                    SourceId = parentTask.SourceId,
                    Url = combined,
                    City = parentTask.City,
                    Category = parentTask.Category,
                    Depth = parentTask.Depth + 1
                });

                if (childUrls.Count >= 10) break;
            }

            if (childUrls.Count > 0)
            {
                _ = _taskQueue.EnqueueBatchTasksAsync(childUrls, CancellationToken.None);
            }
        }
        catch
        {
            // Falha não fatal na extração de URLs filhas
        }
    }
}
