using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous,Route("receitas")]
public sealed class RecipesController(IParaRecipes recipes,IRestaurantReadRepository restaurants):Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] RecipeFilter filter,CancellationToken ct)
    {ViewData["Filter"]=filter;return View(await recipes.ListAsync(filter,false,ct));}
    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug,CancellationToken ct)
    {
        var recipe=await recipes.GetAsync(slug,false,ct);if(recipe is null)return NotFound();
        var related=new List<RestaurantView>();
        foreach(var id in recipe.RestaurantIds){var r=await restaurants.GetPublishedAsync(id,ct);if(r is not null)related.Add(r);}
        ViewData["Restaurants"]=related;return View(recipe);
    }
}
