using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;
[Authorize(Policy="ContentEditor"),Route("admin/receitas")]
public sealed class AdminRecipesController(IParaRecipes recipes,IRestaurantReadRepository restaurants):Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? query,int page=1,CancellationToken ct=default)
    {ViewData["Query"]=query;return View(await recipes.ListAsync(new(Query:query,Page:page),true,ct));}
    [HttpGet("editar")]
    public async Task<IActionResult> Edit(string? slug,string? restaurantSearch,CancellationToken ct)
    {
        var recipe=slug is null?new ParaRecipe():await recipes.GetAsync(slug,true,ct);
        if(recipe is null)return NotFound();
        ViewData["RestaurantSearch"]=restaurantSearch;
        ViewData["Restaurants"]=(await restaurants.SearchAsync(new(Term:restaurantSearch,PageSize:30),ct)).Items;
        return View(recipe);
    }
    [HttpPost("salvar"),ValidateAntiForgeryToken,RequestSizeLimit(65536)]
    public async Task<IActionResult> Save(ParaRecipe recipe,string? ingredientsText,string? stepsText,string? relatedRestaurantIds,CancellationToken ct)
    {
        recipe.Ingredients=Lines(ingredientsText);recipe.Steps=Lines(stepsText);
        recipe.SourceUrl=string.IsNullOrWhiteSpace(recipe.SourceUrl)?null:recipe.SourceUrl.Trim();
        recipe.VideoUrl=string.IsNullOrWhiteSpace(recipe.VideoUrl)?null:recipe.VideoUrl.Trim();
        try
        {
            if(!ModelState.IsValid)throw new ArgumentException("Confira os campos numéricos e os dados obrigatórios.");
            var ids=(relatedRestaurantIds??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            if(ids.Length>12||ids.Any(s=>!long.TryParse(s,out _)))throw new ArgumentException("Use até 12 identificadores separados por vírgula.");
            recipe.RestaurantIds=ids.Select(long.Parse).Distinct().ToArray();
            await recipes.SaveAsync(recipe,User.Identity!.Name!,ct);
            TempData["Flash"]="Receita salva com histórico. "+(recipe.Status=="published"?"A página está publicada.":"O conteúdo permanece fora do catálogo público.");
            return RedirectToAction(nameof(Edit),new{slug=recipe.Slug});
        }
        catch(ArgumentException ex){ViewData["RecipeError"]=ex.Message;}
        catch(MySqlConnector.MySqlException ex)when(ex.Number==1062){ViewData["RecipeError"]="Este endereço de receita já existe. Escolha outro ou reabra a edição existente.";}
        ViewData["Restaurants"]=(await restaurants.SearchAsync(new(PageSize:30),ct)).Items;
        Response.StatusCode=400;return View("Edit",recipe);
    }
    private static string[] Lines(string? value)=>(value??"").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
}
