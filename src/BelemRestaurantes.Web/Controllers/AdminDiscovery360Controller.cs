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
    ILogger<AdminDiscovery360Controller> logger,
    IDiscoveryIntelligenceQueue? intelligenceQueue = null,
    ICommercialMonetizationRepository? monetizationRepo = null,
    IDiscoveryIntelligenceOrchestrator? orchestrator = null) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var coverage = await geoRepo.GetCoverageStatsAsync(ct);
        var pendingEnrichments = await enrichmentQueue.CountPendingAsync(ct);
        var recentChanges = await attributeHistory.ListRecentChangesAsync(25, ct);
        var pendingMedias = await mediaRepo.ListPendingModerationAsync(30, ct);

        var intelligenceStats = intelligenceQueue != null
            ? await intelligenceQueue.GetJobCountsByStatusAsync(ct)
            : new Dictionary<string, int>();

        var recentInvoices = monetizationRepo != null
            ? await monetizationRepo.ListRecentInvoicesAsync(15, ct)
            : Array.Empty<CommercialInvoice>();

        ViewData["CoverageStats"] = coverage;
        ViewData["PendingEnrichments"] = pendingEnrichments;
        ViewData["RecentChanges"] = recentChanges;
        ViewData["PendingMedias"] = pendingMedias;
        ViewData["IntelligenceStats"] = intelligenceStats;
        ViewData["RecentInvoices"] = recentInvoices;

        return View("~/Views/AdminAccount/Discovery360.cshtml");
    }

    [HttpPost("intelligence-batch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunIntelligenceBatch(CancellationToken ct)
    {
        try
        {
            if (orchestrator != null)
            {
                var processed = await orchestrator.RunIntelligenceCycleAsync(10, ct);
                logger.LogInformation("Fila central de inteligência executada por {User}: {Count} itens processados", User.Identity?.Name, processed);
                TempData["Flash"] = $"Fila Central de Inteligência 360 executada: {processed} tarefas processadas.";
            }
            else
            {
                TempData["ErrorMessage"] = "Orquestrador de inteligência não configurado.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar lote de inteligência");
            TempData["ErrorMessage"] = "Erro ao executar fila de inteligência.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("invoice/{txid}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmInvoicePayment(string txid, [FromForm] string authHash, CancellationToken ct)
    {
        if (monetizationRepo != null)
        {
            var success = await monetizationRepo.ConfirmPaymentAsync(txid, authHash, DateTimeOffset.UtcNow, ct);
            if (success)
            {
                logger.LogInformation("Fatura PIX {Txid} confirmada com sucesso por {User}", txid, User.Identity?.Name);
                TempData["Flash"] = $"Fatura {txid} confirmada com autenticação segura HMAC-SHA256.";
            }
            else
            {
                logger.LogWarning("Falha na validação de pagamento para fatura {Txid} por {User}", txid, User.Identity?.Name);
                TempData["ErrorMessage"] = $"Não foi possível confirmar o pagamento da fatura {txid}. Assinatura HMAC inválida ou fatura não encontrada.";
            }
        }
        return RedirectToAction(nameof(Index));
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
