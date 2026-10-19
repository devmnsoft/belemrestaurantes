using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "PlatformOperator")]
public sealed class AdminCuiaDiagnosticsController(
    ICuiaAiService cuia,
    IAiRouter router,
    ICostLedger ledger,
    ILogger<AdminCuiaDiagnosticsController> logger) : Controller
{
    [HttpGet("/admin/cuia-ia")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var status = cuia.GetStatus();
        var health = router.GetHealth();
        IReadOnlyList<CuiaBudgetRow> budgets = [];
        IReadOnlyList<AiCostActivity> recent = [];
        string? ledgerNote = null;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var rows = new List<CuiaBudgetRow>();
            foreach (var provider in status.Providers)
            {
                var spent = await ledger.MonthToDateUsdAsync(provider.Provider, now, ct);
                var open = await ledger.HasBudgetRemaining(provider.Provider, spent, ct);
                rows.Add(new CuiaBudgetRow(provider.Provider, spent, open));
            }
            budgets = rows;
            recent = await ledger.RecentAsync(12, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Painel da Cuia IA não leu o livro de custos ({ExceptionType}).", ex.GetType().Name);
            ledgerNote = "O livro de custos não pôde ser lido agora.";
        }

        return View(new CuiaDiagnosticsView(
            status.Providers,
            health,
            budgets,
            recent,
            CuiaAskMetrics.Read(),
            ledgerNote,
            status.RateLimitPolicy));
    }
}
