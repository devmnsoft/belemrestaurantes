using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Web.Formatting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous]
public sealed class ExternalContentController(IExternalContent content):Controller
{
    [HttpGet("/referencias")]
    public async Task<IActionResult> Index(string? q,string? city,CancellationToken ct)
    {
        if(q?.Length>180||city?.Length>120)return BadRequest();
        ViewData["Query"]=q;ViewData["City"]=city;
        return View(await content.ListAsync(q,city,60,ct));
    }
    [HttpGet("/referencias/{id:long}/previa.svg")]
    [ResponseCache(Duration=60,Location=ResponseCacheLocation.Any)]
    public async Task<IActionResult> Preview(long id,CancellationToken ct)
    {
        var item=await content.GetAsync(id,ct);if(item is null)return NotFound();
        Response.Headers.ContentSecurityPolicy="default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return Content(ReferencePreview.Render(item),"image/svg+xml; charset=utf-8");
    }
}
