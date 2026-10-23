using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.ViewComponents;
public sealed class TrailPreviewViewComponent(IMobilityTrailService trails):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()=>View((await trails.ListTrailsAsync(HttpContext.RequestAborted)).Take(3).ToArray());
}
