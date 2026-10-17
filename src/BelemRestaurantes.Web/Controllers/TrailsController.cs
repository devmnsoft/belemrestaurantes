using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
[Route("roteiros")]
public sealed class TrailsController(IMobilityTrailService trailService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var trails = await trailService.ListTrailsAsync(ct);
        return View(trails);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var trail = await trailService.GetTrailBySlugAsync(slug, ct);
        if (trail == null) return NotFound();
        return View(trail);
    }
}
