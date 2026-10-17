using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin/partner")]
public sealed class AdminPartnerController(
    IPartnerPortalService portalService,
    IMonetizationService monetizationService) : Controller
{
    private string AdminName() => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    [HttpGet("claims")]
    public async Task<IActionResult> Claims(CancellationToken ct)
    {
        var pending = await portalService.ListPendingClaimsAsync(ct);
        return View(pending);
    }

    [HttpPost("claims/{claimId:long}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewClaim(long claimId, bool approve, string? rejectionReason, CancellationToken ct)
    {
        var success = await portalService.ReviewClaimAsync(claimId, approve, rejectionReason, AdminName(), ct);
        if (success)
        {
            TempData["SuccessMessage"] = approve ? "Reivindicação aprovada! O comerciante agora tem acesso à gestão." : "Reivindicação rejeitada com justificativa.";
        }
        else
        {
            TempData["ErrorMessage"] = "Não foi possível processar a reivindicação.";
        }

        return RedirectToAction(nameof(Claims));
    }

    [HttpPost("orders/{orderNumber}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(string orderNumber, CancellationToken ct)
    {
        var success = await monetizationService.ConfirmPaymentAsync(orderNumber, ct);
        if (success)
        {
            TempData["SuccessMessage"] = $"Pagamento do pedido {orderNumber} confirmado e plano ativado com sucesso!";
        }
        else
        {
            TempData["ErrorMessage"] = $"Não foi possível confirmar o pedido {orderNumber}.";
        }

        return RedirectToAction("Claims");
    }
}
