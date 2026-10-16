using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Route("api/restaurants")]
public sealed class RestaurantsController(
    IRestaurantReadRepository repo,
    ISuggestionRepository suggestions,
    ILogger<RestaurantsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? city,
        [FromQuery] string? neighborhood,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken ct = default)
    {
        try
        {
            var query = new RestaurantSearchQuery(
                Term: search,
                City: city,
                Neighborhood: neighborhood,
                Category: category,
                Page: page,
                PageSize: pageSize);

            return Ok(await repo.SearchAsync(query, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Leitura do catálogo falhou");
            return StatusCode(503, new { message = "Catálogo temporariamente indisponível" });
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var result = await repo.GetPublishedAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromForm] SuggestInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var id = await suggestions.AddAsync(input.Name!.Trim(), input.City!.Trim(), input.Website, input.Notes, ct);
        return Ok(new { message = "Sugestão recebida para verificação", id });
    }

    public sealed class SuggestInput
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(160, MinimumLength = 2)]
        public string? Name { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string? City { get; set; }

        [System.ComponentModel.DataAnnotations.Url]
        [System.ComponentModel.DataAnnotations.StringLength(350)]
        public string? Website { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string? Notes { get; set; }
    }
}