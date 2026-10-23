using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.ViewComponents;
public sealed record SearchDiscovery(string Title,string Url,string Kind,string? Context=null,long? CulinaryId=null);
public sealed class SearchDiscoveriesViewComponent(IParaRecipes recipes,IMobilityTrailService trails,ICulinaryResearch culinary):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string query)
    {
        if(query.Length>160||SearchTopics.IsPrivate(query))return View(Array.Empty<SearchDiscovery>());
        query=CulinaryQuery.Correct(query);
        var found=await recipes.ListAsync(new RecipeFilter(Query:query),false,HttpContext.RequestAborted);
        var references=await culinary.FindAsync(query,HttpContext.RequestAborted);
        var normalized=SearchTopics.Normalize(query);
        var itineraries=(await trails.ListTrailsAsync(HttpContext.RequestAborted)).Where(t=>SearchTopics.Normalize(t.Title+" "+t.Region).Contains(normalized)).Take(4);
        var results=found.Items.Take(6).Select(r=>new SearchDiscovery(r.Title,"/receitas/"+Uri.EscapeDataString(r.Slug),"Receita do portal"))
            .Concat(itineraries.Select(t=>new SearchDiscovery(t.Title,"/roteiros/"+Uri.EscapeDataString(t.Slug),"Roteiro do portal")))
            .Concat(references.Select(r=>new SearchDiscovery(r.Title,"/culinaria/referencia/"+r.Id,r.Kind=="dish_evidence"?"Onde comer · prato documentado":r.Kind=="ingredient_offer"?"Ingrediente · preço observado":"Receita de autor · ingredientes e vídeo quando disponível",r.Summary+" Fonte: "+r.Author,r.Id))).ToArray();
        return View(results);
    }
}
