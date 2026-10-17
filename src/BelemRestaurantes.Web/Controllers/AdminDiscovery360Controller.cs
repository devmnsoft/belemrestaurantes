using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin/discovery360")]
public sealed class AdminDiscovery360Controller(
    IGeoMunicipalityRepository geoRepo,
    IEnrichmentQueueRepository enrichmentQueue,
    IRestaurantAttributeHistoryRepository attributeHistory,
    IRestaurantMediaRepository mediaRepo,
    IEnrichmentService enrichmentService,
    ICrawlerTaskQueue taskQueue,
    ICrawlerSourceRepository sourceRepo,
    ILogger<AdminDiscovery360Controller> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var coverage = await geoRepo.GetCoverageStatsAsync(ct);
        var pendingEnrichments = await enrichmentQueue.CountPendingAsync(ct);
        var recentChanges = await attributeHistory.ListRecentChangesAsync(25, ct);
        var pendingMedias = await mediaRepo.ListPendingModerationAsync(30, ct);

        ViewData["CoverageStats"] = coverage;
        ViewData["PendingEnrichments"] = pendingEnrichments;
        ViewData["RecentChanges"] = recentChanges;
        ViewData["PendingMedias"] = pendingMedias;

        return View("~/Views/AdminAccount/Discovery360.cshtml");
    }

    [HttpPost("enrich-batch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunEnrichmentBatch(CancellationToken ct)
    {
        try
        {
            var processed = await enrichmentService.RunEnrichmentCycleAsync(10, ct);
            logger.LogInformation("Lote de enriquecimento 360 executado por {User}: {Count} estabelecimentos processados", User.Identity?.Name, processed);
            TempData["Flash"] = $"Enriquecimento inteligente executado com sucesso: {processed} estabelecimentos atualizados.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar enriquecimento em lote");
            TempData["ErrorMessage"] = "Ocorreu um erro ao processar o enriquecimento inteligente.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("media/{id:long}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveMedia(long id, CancellationToken ct)
    {
        var ok = await mediaRepo.ApproveMediaAsync(id, ct);
        if (ok)
        {
            logger.LogInformation("Mídia #{MediaId} aprovada por {User}", id, User.Identity?.Name);
            TempData["Flash"] = $"Mídia #{id} aprovada com sucesso para exibição na galeria.";
        }
        else
        {
            TempData["ErrorMessage"] = "Mídia não encontrada.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("media/{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectMedia(long id, CancellationToken ct)
    {
        var ok = await mediaRepo.RejectMediaAsync(id, ct);
        if (ok)
        {
            logger.LogInformation("Mídia #{MediaId} rejeitada por {User}", id, User.Identity?.Name);
            TempData["Flash"] = $"Mídia #{id} rejeitada com sucesso.";
        }
        else
        {
            TempData["ErrorMessage"] = "Mídia não encontrada.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("prioritize-city")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrioritizeCity([FromForm] uint ibgeCode, CancellationToken ct)
    {
        var muni = await geoRepo.GetByIbgeCodeAsync(ibgeCode, ct);
        if (muni == null)
        {
            TempData["ErrorMessage"] = "Município não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var sources = await sourceRepo.ListSourcesAsync("active", ct);
            int enqueued = 0;
            foreach (var s in sources)
            {
                var enq = await taskQueue.EnqueueTaskAsync(new CrawlerTask
                {
                    SourceId = s.Id,
                    Url = s.BaseUrl,
                    City = muni.Name,
                    Category = s.DefaultCategory,
                    Depth = 0,
                    NextAttemptAt = DateTimeOffset.UtcNow
                }, ct);
                if (enq) enqueued++;
            }

            await geoRepo.UpdateScanTimestampAsync(ibgeCode, DateTimeOffset.UtcNow, "in_progress", ct);

            logger.LogInformation("Priorização de varredura para {City} ({IbgeCode}) por {User}: {Tasks} tarefas criadas", muni.Name, ibgeCode, User.Identity?.Name, enqueued);
            TempData["Flash"] = $"Priorização de varredura para o município de {muni.Name} registrada com sucesso ({enqueued} tarefas criadas).";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao priorizar município {IbgeCode}", ibgeCode);
            TempData["ErrorMessage"] = "Falha ao priorizar varredura do município.";
        }

        return RedirectToAction(nameof(Index));
    }
}
