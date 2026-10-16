using BelemRestaurantes.Web.Data;
using BelemRestaurantes.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
namespace BelemRestaurantes.Web.Controllers;
[Route("api/restaurants")]
public sealed class RestaurantsController(RestaurantRepository repo,ILogger<RestaurantsController> logger):Controller {
 [HttpGet] public async Task<IActionResult> List([FromQuery,StringLength(100)]string? search,[FromQuery,StringLength(80)]string? city,[FromQuery]int limit=30) {try{return Ok(await repo.Search(city,search,limit));}catch(Exception e){logger.LogError(e,"Catalog read failed");return StatusCode(503,new{message="Catálogo temporariamente indisponível"});}}
 [HttpGet("{id:long}")] public async Task<IActionResult> Details(long id) {var r=await repo.Find(id);return r is null?NotFound():Ok(r);}
 [HttpPost("suggest")][ValidateAntiForgeryToken] public async Task<IActionResult> Suggest([FromForm]SuggestInput input) {if(!ModelState.IsValid)return BadRequest(ModelState);await repo.AddSuggestion(new(input.Name!.Trim(),input.City!.Trim(),input.Website,input.Notes));return Ok(new{message="Sugestão recebida para verificação"});}
 public sealed class SuggestInput {[Required,StringLength(160,MinimumLength=2)]public string? Name{get;set;}[Required,StringLength(100)]public string? City{get;set;}[Url,StringLength(350)]public string? Website{get;set;}[StringLength(500)]public string? Notes{get;set;}}
}