using System.Globalization;
using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;

public sealed record GeocodingAdminPage(GeocodingSummary Summary, IReadOnlyList<GeocodingTaskRow> Tasks, int Total, int Page, int Pages, string? State, string? Search);

/// <summary>Sprint 59 — acompanhamento da geocodificação autônoma (Sprint 58): contagens, fila com motivos,
/// e devolução à fila de rejeições/falhas terminais. Sprint 60 — revisão humana: reverte coordenada aplicada
/// pelo OSM com histórico e auditoria. Sprint 63 — distribuição de motivos de rejeição e devolução à fila
/// por motivo (quando uma mudança de capacidade torna o caso recuperável). Toda ação humana fica em audit_events.</summary>
[Authorize(Policy="RestaurantModerator"), Route("admin/geocodificacao")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminGeocodingController(IGeocodingQueue queue, IRestaurantAttributeHistoryRepository history) : Controller
{
    private const int PageSize = 25;
    private const int StaleDays = 7;

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, string? state = null, string? q = null, CancellationToken ct = default)
    {
        try
        {
            var summary = await queue.SummaryAsync(ct);
            var (items, total) = await queue.ListTasksAsync(state, q, page, PageSize, ct);
            return View(new GeocodingAdminPage(summary, items, total, page, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)), state, q));
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("{id:long}/reenfileirar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Requeue(long id, CancellationToken ct)
    {
        var ok = await queue.RequeueAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok
            ? "Ficha devolvida à fila de geocodificação; o ciclo do Worker respeita o teto diário."
            : "Só rejeição ou falha terminais de ficha ainda sem coordenada volta à fila.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("reenfileirar-antigas"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RequeueStale(CancellationToken ct)
    {
        var n = await queue.RequeueStaleTerminalsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, StaleDays, ct);
        TempData["SuccessMessage"] = n == 0
            ? $"Nenhuma rejeição ou falha com {StaleDays} dias ou mais para devolver à fila."
            : $"{n} ficha(s) com {StaleDays} dias ou mais devolvida(s) à fila; o teto diário do Worker limita o ritmo.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Devolve à fila todas as linhas terminais com um motivo de rejeição específico — útil quando uma
    /// mudança de capacidade (ex.: o fallback Overpass da Sprint 62) torna esse motivo recuperável agora.</summary>
    [HttpPost("reenfileirar-por-motivo"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RequeueByReason([FromForm] string? motivo, CancellationToken ct)
    {
        try
        {
            var n = await queue.RequeueByReasonAsync(motivo ?? "", User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
            TempData["SuccessMessage"] = n == 0
                ? $"Nenhuma rejeição ou falha terminal com o motivo \"{motivo}\" para devolver à fila."
                : $"{n} ficha(s) com o motivo \"{motivo}\" devolvida(s) à fila; o teto diário do Worker limita o ritmo.";
        }
        catch (ArgumentException)
        {
            TempData["ErrorMessage"] = "Motivo de rejeição inválido; nada foi alterado.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:long}/reverter"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(long id, CancellationToken ct)
    {
        var result = await queue.RevertAppliedCoordinatesAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (result is null)
        {
            TempData["ErrorMessage"] = "Nada para reverter: só coordenada aplicada pelo OpenStreetMap/Nominatim ou já ajustada manualmente em ficha sem proteção Tier 1 é removida aqui.";
            return RedirectToAction(nameof(Index));
        }
        await history.RecordHistoryAsync(new RestaurantAttributeHistory
        {
            RestaurantId = id,
            AttributeName = "coordinate",
            OldValue = $"{result.Latitude.ToString("0.0000000", CultureInfo.InvariantCulture)};{result.Longitude.ToString("0.0000000", CultureInfo.InvariantCulture)}",
            NewValue = "(removido por revisão administrativa)",
            SourceType = "admin_review",
            SourceReference = result.Source,
            ConfidenceScore = 1.000m,
            RequiresModeration = false,
            IsApplied = true
        }, ct);
        TempData["SuccessMessage"] = "Coordenada removida e ficha marcada como rejeição revisada. Use \"Re-enfileirar\" quando houver o que conferir de novo (endereço corrigido, mapa melhorado).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Revisão humana de coordenada aplicada: substitui o par por um conferido pelo operador
    /// (mesmo padrão de escrita e auditoria do apply automático, origem admin_review).</summary>
    [HttpPost("{id:long}/ajustar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(long id, [FromForm] string? latitude, [FromForm] string? longitude, [FromForm] string? precision, CancellationToken ct)
    {
        latitude = latitude?.Trim().Replace(',', '.');
        longitude = longitude?.Trim().Replace(',', '.');
        if (!double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            TempData["ErrorMessage"] = "Informe latitude e longitude numéricas para o ajuste (o formato da linha pode ser usado como ponto de partida).";
            return RedirectToAction(nameof(Index));
        }
        if (precision is not ("rooftop" or "approximate"))
        {
            TempData["ErrorMessage"] = "Escolha a precisão declarada do ajuste: rooftop (confiança de nível de edifício) ou approximate (região confirmada).";
            return RedirectToAction(nameof(Index));
        }
        if (!GeocodingRules.IsCoordinateInRange(lat, lng))
        {
            TempData["ErrorMessage"] = "O par informado fica fora da caixa de contorno do Pará; nada foi alterado.";
            return RedirectToAction(nameof(Index));
        }
        var result = await queue.AdjustAppliedCoordinatesAsync(id, lat, lng, precision, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (result is null)
        {
            TempData["ErrorMessage"] = "Nada para ajustar: só ficha com coordenada aplicada pelo OpenStreetMap/Nominatim ou já ajustada manualmente, sem proteção Tier 1, recebe revisão aqui.";
            return RedirectToAction(nameof(Index));
        }
        await history.RecordHistoryAsync(new RestaurantAttributeHistory
        {
            RestaurantId = id,
            AttributeName = "coordinate",
            OldValue = $"{result.OldLatitude.ToString("0.0000000", CultureInfo.InvariantCulture)};{result.OldLongitude.ToString("0.0000000", CultureInfo.InvariantCulture)}",
            NewValue = $"{lat.ToString("0.0000000", CultureInfo.InvariantCulture)};{lng.ToString("0.0000000", CultureInfo.InvariantCulture)}",
            SourceType = "admin_review",
            SourceReference = $"ajuste administrativo substituiu: {result.OldSource}",
            ConfidenceScore = 1.000m,
            RequiresModeration = false,
            IsApplied = true
        }, ct);
        TempData["SuccessMessage"] = "Coordenadas ajustadas e trilha registrada. A ficha fica com origem \"ajuste administrativo\" e nenhuma tentativa automática do Worker a sobrescreve; use \"Reverter coordenada\" se quiser devolver ao processo.";
        return RedirectToAction(nameof(Index));
    }
}
