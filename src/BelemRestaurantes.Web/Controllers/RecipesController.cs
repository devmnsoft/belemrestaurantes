using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous,Route("receitas")]
public sealed class RecipesController(IParaRecipes recipes,IRestaurantReadRepository restaurants,IPlaceDossier? videos=null,ICulinaryResearch? culinary=null):Controller
{
    [HttpGet("/culinaria")]
    public async Task<IActionResult> Videos(CancellationToken ct)
        => View(videos is null ? Array.Empty<CulinaryVideo>() : await videos.ListVideosAsync(null, ct));
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] RecipeFilter filter,CancellationToken ct)
    {
        ViewData["Filter"]=filter;
        ViewData["References"]=culinary is null?Array.Empty<CulinaryReference>():await culinary.FindAsync(string.IsNullOrWhiteSpace(filter.Query)?"receitas":filter.Query,ct);
        return View(await recipes.ListAsync(filter,false,ct));
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug,CancellationToken ct)
    {
        var recipe=await recipes.GetAsync(slug,false,ct);if(recipe is null)return NotFound();
        var related=new List<RestaurantView>();
        foreach(var id in recipe.RestaurantIds){var r=await restaurants.GetPublishedAsync(id,ct);if(r is not null)related.Add(r);}
        ViewData["Restaurants"]=related;
        ViewData["Videos"]=videos is null?Array.Empty<CulinaryVideo>():await videos.ListVideosAsync(recipe.Title,ct);
        return View(recipe);
    }
}
