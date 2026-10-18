using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="RestaurantModerator")]
[Route("admin/moderacao")]
public sealed class AdminBulkController(MySqlBulkModeration moderation) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ModerationFilter filter,int page=1,CancellationToken ct=default)
    {
        ViewData["Filter"]=filter; ViewData["BatchLimit"]=moderation.Limit;
        return View(await moderation.ListAsync(filter,page,ct));
    }
    [HttpPost("preparar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Prepare(ModerationFilter filter,string operation,string? reason,long[] ids,bool allFiltered,CancellationToken ct)
    {
        try { return View("Confirm",await moderation.PrepareAsync(User.Identity!.Name!,operation,reason,ids,allFiltered,filter,ct)); }
        catch(ArgumentException ex) { TempData["ErrorMessage"]=ex.Message; return RedirectToAction(nameof(Index),filter); }
    }
    [HttpPost("confirmar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string id,bool confirmed,CancellationToken ct)
    {
        if(!confirmed || !Guid.TryParse(id,out _)) return BadRequest();
        try { var result=await moderation.ExecuteAsync(id,User.Identity!.Name!,ct); return result is null?NotFound():View("Result",result); }
        catch(ArgumentException ex) { TempData["ErrorMessage"]=ex.Message; return RedirectToAction(nameof(Index)); }
    }
}
