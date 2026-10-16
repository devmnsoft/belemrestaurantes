using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Route("establishment")]
public sealed class EstablishmentController(
    IRestaurantReadRepository repo) : Controller
{
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var establishment = await repo.GetPublishedAsync(id, ct);

        if (establishment is null)
        {
            return NotFound();
        }

        return View(establishment);
    }
}