using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin/crawler")]
public sealed class AdminCrawlerController(
    ICrawlerSourceRepository sourceRepo,
    ICrawlerTelemetryRepository telemetryRepo,
    ICrawlerOrchestrator orchestrator) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var stats = await telemetryRepo.GetSummaryStatsAsync(ct);
        var sources = await sourceRepo.ListSourcesAsync(null, ct);
        var recentRuns = await telemetryRepo.ListRecentRunsAsync(30, ct);

        ViewData["Stats"] = stats;
        ViewData["RecentRuns"] = recentRuns;

        return View("~/Views/AdminAccount/Crawler.cshtml", sources);
    }

    [HttpPost("sources/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSource(
        [FromForm] string name,
        [FromForm] string baseUrl,
        [FromForm] string sourceType,
        [FromForm] string? targetCity,
        [FromForm] string? defaultCategory,
        [FromForm] int crawlDelay = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
        {
            TempData["ErrorMessage"] = "Nome da fonte e URL base são obrigatórios.";
            return RedirectToAction(nameof(Index));
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            TempData["ErrorMessage"] = "A URL informada deve ser absoluta com protocolo HTTP ou HTTPS.";
            return RedirectToAction(nameof(Index));
        }

        // Validação anti-SSRF preventiva
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            if (addresses.Length == 0 || addresses.All(SafeHttpClient.IsDisallowedIp))
            {
                TempData["ErrorMessage"] = $"Acesso bloqueado por segurança SSRF: O endereço '{uri.Host}' aponta para IP interno ou restrito.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Não foi possível resolver o domínio informado ({uri.Host}): {ex.Message}";
            return RedirectToAction(nameof(Index));
        }

        var source = new CrawlerSource
        {
            Name = name.Trim(),
            BaseUrl = baseUrl.Trim(),
            SourceType = sourceType,
            TargetCity = string.IsNullOrWhiteSpace(targetCity) ? null : targetCity.Trim(),
            DefaultCategory = string.IsNullOrWhiteSpace(defaultCategory) ? null : defaultCategory.Trim(),
            Status = "active",
            CrawlDelaySeconds = Math.Clamp(crawlDelay, 2, 60)
        };

        try
        {
            var id = await sourceRepo.CreateSourceAsync(source, ct);
            TempData["Flash"] = $"Fonte #{id} ({source.Name}) cadastrada com sucesso e pronta para rastreamento gentil.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Erro ao cadastrar fonte: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sources/{id:long}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSourceStatus(long id, [FromForm] string status, CancellationToken ct)
    {
        if (status != "active" && status != "paused" && status != "blocked")
        {
            TempData["ErrorMessage"] = "Status inválido.";
            return RedirectToAction(nameof(Index));
        }

        var ok = await sourceRepo.UpdateStatusAsync(id, status, ct);
        if (ok)
        {
            TempData["Flash"] = $"Status da fonte #{id} atualizado para '{status}'.";
        }
        else
        {
            TempData["ErrorMessage"] = $"Fonte #{id} não encontrada.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("run-cycle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunManualCycle(CancellationToken ct)
    {
        try
        {
            var processed = await orchestrator.RunCrawlerCycleAsync(10, ct);
            TempData["Flash"] = $"Ciclo do Cuia Crawler executado com sucesso: {processed} tarefas processadas.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Falha ao executar ciclo manual: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
