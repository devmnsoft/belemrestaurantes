using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin/crawler")]
public sealed class AdminCrawlerController(
    ICrawlerSourceRepository sourceRepo,
    ICrawlerTelemetryRepository telemetryRepo,
    ICrawlerTaskQueue taskQueue,
    ILogger<AdminCrawlerController> logger) : Controller
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
            if (addresses.Length == 0 || addresses.Any(SafeHttpClient.IsDisallowedIp))
            {
                logger.LogWarning("Tentativa de cadastro de fonte bloqueada por segurança SSRF: {Host} para {Url}", uri.Host, baseUrl);
                TempData["ErrorMessage"] = "Acesso bloqueado por segurança SSRF: O endereço informado aponta para IP interno, reservado ou restrito.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na resolução de DNS para a fonte {Host}", uri.Host);
            TempData["ErrorMessage"] = "Não foi possível validar o domínio informado no serviço de nomes.";
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
            logger.LogInformation("Nova fonte #{SourceId} cadastrada por {User}: {SourceName} ({BaseUrl})", id, User.Identity?.Name, source.Name, source.BaseUrl);
            TempData["Flash"] = $"Fonte #{id} ({source.Name}) cadastrada com sucesso e pronta para rastreamento gentil.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gravar fonte {SourceName} no banco de dados", source.Name);
            TempData["ErrorMessage"] = "Não foi possível cadastrar a fonte no momento. Tente novamente mais tarde.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sources/{id:long}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSourceStatus(long id, [FromForm] string status, CancellationToken ct)
    {
        if (status != "active" && status != "paused" && status != "blocked")
        {
            TempData["ErrorMessage"] = "Status de fonte inválido.";
            return RedirectToAction(nameof(Index));
        }

        var ok = await sourceRepo.UpdateStatusAsync(id, status, ct);
        if (ok)
        {
            logger.LogInformation("Status da fonte #{SourceId} alterado para {Status} por {User}", id, status, User.Identity?.Name);
            TempData["Flash"] = $"Status da fonte #{id} atualizado para '{status}'.";
        }
        else
        {
            TempData["ErrorMessage"] = "Fonte não encontrada.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("run-cycle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunManualCycle(CancellationToken ct)
    {
        // Requisito Sprint 09: "O acionamento manual do Crawler deve registrar uma tarefa persistente, sem manter uma requisição HTTP aberta durante toda a pesquisa."
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
                    City = s.TargetCity ?? "Belém",
                    Category = s.DefaultCategory,
                    Depth = 0,
                    NextAttemptAt = DateTimeOffset.UtcNow
                }, ct);

                if (enq) enqueued++;
            }

            logger.LogInformation("Acionamento manual do crawler por {User}: {Count} tarefas enfileiradas para processamento em background", User.Identity?.Name, enqueued);
            TempData["Flash"] = $"Solicitação de pesquisa autônoma registrada com sucesso! {enqueued} tarefas foram priorizadas na fila do Worker Windows.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao registrar acionamento manual do crawler.");
            TempData["ErrorMessage"] = "Não foi possível registrar a solicitação de rastreamento no momento.";
        }

        return RedirectToAction(nameof(Index));
    }
}
