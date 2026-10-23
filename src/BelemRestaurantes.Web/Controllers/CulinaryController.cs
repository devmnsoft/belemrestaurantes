using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class CulinaryController(ICulinaryResearch research) : Controller
{
    [HttpGet("/culinaria/referencia/{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var item = await research.GetAsync(id, ct);
        return item is null ? NotFound() : View(item);
    }
}
