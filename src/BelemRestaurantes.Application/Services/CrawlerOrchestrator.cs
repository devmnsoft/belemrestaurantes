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
    private readonly IPlaceReferences? _references;
    private readonly IExternalContent? _externalContent;
    private readonly AutoPublicationOptions _autoPublication;
    private int _autoPublished;
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
        IGeoMunicipalityRepository? geoRepo = null,IPlaceReferences? references = null, AutoPublicationOptions? autoPublication=null,IExternalContent? externalContent=null)
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
        _references=references;
        _autoPublication=autoPublication??new();
        _externalContent=externalContent;
    }

    public async Task MaintainQueueAsync(CancellationToken ct) =>
        await _taskQueue.ReleaseStaleLocksAsync(_clock.UtcNow, ct);

    public async Task<int> RunCrawlerCycleAsync(int maxTasks, CancellationToken ct)
    {
        _autoPublished=0;
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
                    if(_externalContent is not null && download.ContentType?.Contains("html")==true)
                        await _externalContent.ObservePageAsync(download.ContentText,task.Url,task.City,ct);
                    // C. Extração de estabelecimentos (Schema.org / Heurística)
                    var contentOnly=(await _sources.GetByIdAsync(task.SourceId,ct))?.ContentOnly==true;
                    var restaurants = contentOnly?Array.Empty<ExtractedRestaurantData>():_parser.ExtractRestaurants(download.ContentText, task.Url, task.City);
                    discoveredInTask = restaurants.Count;

                    foreach (var extracted in restaurants)
                    {
                        var ingested = await ProcessExtractedRestaurantAsync(extracted, task, ct);
                        if (ingested) ingestedInTask++;
                    }

                    // Se a página for um diretório HTML, extrair links de sitemap ou páginas filhas
                    if (!contentOnly && task.Depth < 2 && download.ContentType != null && download.ContentType.Contains("html"))
                    {
                        // Enfileirar no máximo 10 URLs filhas do mesmo domínio
                        await EnqueueChildLinksAsync(task, download.ContentText, ct);
                    }

                    await _taskQueue.CompleteTaskAsync(task.Id, ct);
                    await _sources.RecordCrawlResultAsync(task.SourceId, true, null, download.ETag, download.LastModified, ct);
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
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
            // Public editorial feeds can be checked each minute; restaurant sources retain six-hour freshness.
            if (source.LastCrawledAt == null || source.LastCrawledAt < DateTimeOffset.UtcNow.Subtract(source.ContentOnly?TimeSpan.FromMinutes(1):TimeSpan.FromHours(6)))
            {
                var (allowed, sourceDelay) = await _robotsTxtService.CheckAccessAsync(source.BaseUrl, "CuiaCrawler", ct);
                if (!allowed)
                {
                    _logger.LogWarning("Crawler: fonte {SourceId}, cidade {City}, não consultada por robots.txt.", source.Id, source.TargetCity);
                    await _sources.RecordCrawlResultAsync(source.Id, false, "Bloqueado por robots.txt", null, null, ct);
                    continue;
                }
                if(source.ContentOnly && sourceDelay>10){await _sources.RecordCrawlResultAsync(source.Id,false,"Cadência da origem exige agendamento específico",null,null,ct);continue;}
                if(source.ContentOnly && sourceDelay>0)await Task.Delay(TimeSpan.FromSeconds(sourceDelay),ct);
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
                        var entries=_parser.ParseRssFeed(download.ContentText, source.BaseUrl);
                        if(source.ContentOnly)entries=entries.Where(e=>ExternalContentRules.Relevant(e.Title,source.TargetCity??"Belém")&&ExternalContentRules.Regional(e.Title+" "+e.Description,source.TargetCity??"Belém")).Take(20).ToArray();
                        if(source.ContentOnly && _externalContent is not null)
                            foreach(var entry in entries)await _externalContent.ObserveAsync(new ExternalContent{Title=entry.Title,Url=entry.Url,City=source.TargetCity??"Belém",Summary=entry.Description??"",VideoUrl=ExternalContentRules.FindVideoLink(entry.Description??""),ObservedAt=_clock.UtcNow.UtcDateTime},ct);
                        urls = entries.Select(x=>x.Url).ToArray();
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
                    _logger.LogWarning("Crawler: fonte {SourceId}, cidade {City}, falha HTTP {Status}: {Reason}",
                        source.Id, source.TargetCity, download.StatusCode, download.ErrorMessage);
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
        var identity = $"{extracted.SourceUrl}|{NameNormalizer.Normalize(extracted.Name)}|{NameNormalizer.Normalize(city)}|{extracted.Address}";
        var externalId = "crawler:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
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
        ) { Phone = extracted.Telephone, Instagram = extracted.Instagram, Facebook = extracted.Facebook,
            TikTok = extracted.TikTok, YouTube = extracted.YouTube };

        var write = await _ingestion.UpsertWithOutcomeAsync(
            place,
            provider: $"crawler:{task.SourceId}",
            now: _clock.UtcNow,
            ct: ct);
        var restaurantId=write.Id;

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

        if(_references is not null && (extracted.MenuItems.Count>0 || extracted.OpeningHours.Count>0 || extracted.YouTube is not null))
        {
            await _references.ObserveAsync(new(0,extracted.Name,city,extracted.SourceUrl,"structured_data",new Uri(extracted.SourceUrl).Host,
                _clock.UtcNow,_clock.UtcNow.AddDays(7),extracted.MenuItems,extracted.Address,
                extracted.YouTube is null?[]:[extracted.YouTube],OpeningHours:extracted.OpeningHours),ct);
        }

        // Só guarda imagem do próprio site, e ainda assim pendente: schema.org não é licença.
        if (_mediaRepo != null && LicensedImageRules.TryOfficialSiteImage(extracted.SourceUrl, extracted.PhotoUrl, out var officialImage))
        {
            await _mediaRepo.AddMediaAsync(new RestaurantMedia
            {
                RestaurantId = restaurantId,
                MediaType = "photo_facade",
                MediaUrl = officialImage,
                PriorityTier = (int)MediaPriorityTier.LicensedExternal,
                SourceOrigin = extracted.SourceUrl,
                AttributionText = "Imagem publicada no site do estabelecimento. A licença ainda não foi conferida por uma pessoa.",
                LicenseType = "site_a_conferir",
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
        if ((write.Created && dedupResult.RequiresHumanReview) || extracted.HasConflict)
        {
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DataConflict,
                $"Fonte autônoma {extracted.SourceUrl} encontrou divergência: {dedupResult.Rationale}",
                ct);
        }
        else if (write.Created && dedupResult.Outcome == MatchOutcome.ProbableDuplicate)
        {
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.DuplicateSuspected,
                $"Suspeita de duplicidade com estabelecimento #{dedupResult.MatchedRestaurantId}: {dedupResult.Rationale}",
                ct);
        }
        else if(write.Created)
        {
            var source=await _sources.GetByIdAsync(task.SourceId,ct);
            var blocked=AutoPublicationRules.BlockReason(extracted,source,dedupResult);
            if(_autoPublication.Enabled && _autoPublished<Math.Clamp(_autoPublication.MaxPerCycle,0,10) && blocked is null)
            {
                if(await _moderation.ApproveAsync(restaurantId,"system:external-auto-publication",ct))
                {_autoPublished++;_logger.LogInformation("Descoberta externa {RestaurantId} publicada automaticamente; fonte {SourceId}.",restaurantId,task.SourceId);return true;}
                blocked="A validação transacional de publicação recusou o registro.";
            }
            await _moderation.EnqueueAsync(
                "restaurant",
                restaurantId,
                ModerationReason.NewDiscovery,
                $"Descoberta externa via {extracted.SourceUrl} ({city}). Publicação automática: {blocked??(_autoPublication.Enabled?"limite do ciclo":"desabilitada")}",
                ct);
        }

        return true;
    }

    private async Task EnqueueChildLinksAsync(CrawlerTask parentTask, string htmlContent, CancellationToken ct)
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
                await _taskQueue.EnqueueBatchTasksAsync(childUrls, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enfileirar URLs filhas da fonte {SourceId}", parentTask.SourceId);
        }
    }
}
