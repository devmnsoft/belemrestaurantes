using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BelemRestaurantes.Web.Models;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="RestaurantModerator")]
[Route("admin/moderacao")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminBulkController(MySqlBulkModeration moderation) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ModerationFilter filter,int page=1,CancellationToken ct=default)
    {
        filter=filter with { PageSize=filter.SafePageSize };
        ViewData["Filter"]=filter; ViewData["BatchLimit"]=moderation.Limit;
        return View(await moderation.ListAsync(filter,page,ct));
    }
    [HttpPost("preparar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Prepare(ModerationFilter filter,string operation,string? reason,long[] ids,bool allFiltered,int page,CancellationToken ct)
    {
        ViewData["ReturnUrl"]=ModerationNavigation.PageUrl(filter,allFiltered?1:page);
        ViewData["SelectionKey"]=ModerationNavigation.SelectionKey(filter);
        try { return View("Confirm",await moderation.PrepareAsync(User.Identity!.Name!,operation,reason,ids,allFiltered,filter,ct)); }
        catch(ArgumentException ex) { TempData["ErrorMessage"]=ex.Message; return Redirect(ModerationNavigation.PageUrl(filter,page)); }
    }
    [HttpGet("lote/{id}")]
    public async Task<IActionResult> Result(string id,CancellationToken ct)
    {
        if(!Guid.TryParse(id,out _))return BadRequest();
        var result=await moderation.GetResultAsync(id,User.Identity!.Name!,ct);
        return result is null?NotFound():View("Result",result);
    }
    [HttpPost("confirmar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string id,bool confirmed,string? returnUrl,CancellationToken ct)
    {
        ViewData["ReturnUrl"]=ModerationNavigation.SafeReturn(returnUrl);
        if(!confirmed || !Guid.TryParse(id,out _)) return BadRequest();
        try { var result=await moderation.ExecuteAsync(id,User.Identity!.Name!,ct); return result is null?NotFound():View("Result",result); }
        catch(ArgumentException ex) { TempData["ErrorMessage"]=ex.Message; return Redirect(ModerationNavigation.SafeReturn(returnUrl)); }
    }
}
