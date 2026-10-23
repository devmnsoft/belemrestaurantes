using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.ViewComponents;
public sealed class SearchTopicsViewComponent(ISearchArchive archive,ILogger<SearchTopicsViewComponent> logger):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        IReadOnlyList<SearchTopic> topics=[];
        try{topics=await archive.TopicsAsync(HttpContext.RequestAborted);}
        catch(OperationCanceledException)when(HttpContext.RequestAborted.IsCancellationRequested){throw;}
        catch(Exception ex){logger.LogWarning("Assuntos indisponíveis: {Type}",ex.GetType().Name);}
        return View(topics);
    }
}
