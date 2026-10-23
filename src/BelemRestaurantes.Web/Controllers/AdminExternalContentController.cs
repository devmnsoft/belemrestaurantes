using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;
[Authorize(Policy="RestaurantModerator"),Route("admin/conteudos-externos")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminExternalContentController(IExternalContent content):Controller
{
    [HttpGet]public async Task<IActionResult> Index(CancellationToken ct)=>View(await content.ListAsync(null,null,60,ct,true));
    [HttpPost("{id:long}/estado"),ValidateAntiForgeryToken]
    public async Task<IActionResult> State(long id,string state,CancellationToken ct)
    {
        if(state is not ("published" or "suspended"))return BadRequest();
        await content.SetStateAsync(id,state,User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct);return RedirectToAction(nameof(Index));
    }
}
