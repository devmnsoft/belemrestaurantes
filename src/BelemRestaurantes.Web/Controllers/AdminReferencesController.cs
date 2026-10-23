using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="RestaurantModerator")]
[Route("admin/referencias")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminReferencesController(IPlaceReferences references) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct)=>View(await references.ListPendingAsync(ct));
    [HttpPost("{id:long}/revisar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(long id,string decision,CancellationToken ct)
    {
        if(decision is not ("approved" or "rejected"))return BadRequest();
        await references.ReviewAsync(id,decision,User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct);
        return RedirectToAction(nameof(Index));
    }
}
