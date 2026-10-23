using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous]
public sealed class FederatedSearchController(IWebSearch search,IExternalContent content,ISearchArchive archive,ILogger<FederatedSearchController> logger,ICulinaryResearch culinary):Controller
{
    [HttpGet("/api/busca/fontes")]
    [EnableRateLimiting("cuia")]
    public async Task<IActionResult> Sources(string? q,string? city,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(q)||q.Length>160||city?.Length>120||SearchTopics.IsPrivate(q))return BadRequest();
        q=CulinaryQuery.Correct(q);
        await SearchTopicRecorder.RecordAsync(HttpContext,archive,q,ct);
        var culinaryItems=await culinary.FindAsync(q,ct);
        var local=await content.ListAsync(q,city,8,ct);
        WebSearchResult remote;
        try{remote=await search.SearchAsync(q+(string.IsNullOrWhiteSpace(city)?" Pará":" "+city+" Pará"),ct);}
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){logger.LogWarning("Fontes externas indisponíveis: {Type}",ex.GetType().Name);remote=new("unavailable",[]);}
        var items=SearchRanking.Merge(q,local.Select(s=>new WebSearchSource(s.Title,s.Url,s.Facts.Description??s.Summary,new DateTimeOffset(DateTime.SpecifyKind(s.ObservedAt,DateTimeKind.Utc))){Publisher=s.Publisher,SourceKind="collected_reference",PublishedAt=s.PublishedAt is null?null:new DateTimeOffset(DateTime.SpecifyKind(s.PublishedAt.Value,DateTimeKind.Utc))})
            .Concat(SearchRanking.ForKnownPlace(q,local.Select(s=>s.Facts.PlaceName),remote.Sources)));
        if(CulinaryQuery.IsCulinary(q))await culinary.EnqueueAsync(items.Where(s=>s.SourceKind!="encyclopedia").Select(s=>s.Url),CulinaryQuery.Topic(q),ct);
        return Json(new{status=remote.Status,query=q,culinary=culinaryItems,sources=items});
    }
}
