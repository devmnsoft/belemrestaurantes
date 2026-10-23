using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.ViewComponents;
public sealed class ExternalContentPreviewViewComponent(IExternalContent content):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()=>View(await content.ListAsync(null,null,3,HttpContext.RequestAborted));
}
