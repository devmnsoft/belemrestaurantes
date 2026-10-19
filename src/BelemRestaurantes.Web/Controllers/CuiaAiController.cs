using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class CuiaAiController(ICuiaAiService cuiaAi, IRestaurantReadRepository restaurantRepo) : Controller
{
    [HttpGet("/cuia-ia")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? city, CancellationToken ct)
    {
        ViewData["Title"] = "Cuia IA — Sommelier & Concierge Gastronômico da Amazônia";
        ViewData["MetaDescription"] = "Consulte a Inteligência Artificial especializada na culinária paraense. Recomendações de restaurantes reais, harmonizações de sabores da floresta, itinerários ribeirinhos e dicas do chef.";

        var cities = await restaurantRepo.ListCitiesAsync(ct);
        ViewData["Cities"] = cities;
        ViewData["InitialPrompt"] = q ?? "";
        ViewData["InitialCity"] = city ?? "";

        // Se houver busca inicial via query string, processa a resposta inicial
        CuiaAiResponse? initialResponse = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            initialResponse = await cuiaAi.AskAsync(new CuiaAiQuery(q, city), ct);
        }

        return View(initialResponse);
    }

    public sealed record AskRequest(string Prompt, string? City, string? Occasion, string? Dietary, string? Budget);

    [HttpPost("/api/cuia-ia/ask")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Prompt))
        {
            return BadRequest(new { error = "Por favor, digite sua dúvida ou preferência gastronômica." });
        }

        if (req.Prompt.Length > 500)
        {
            return BadRequest(new { error = "A consulta pode ter no máximo 500 caracteres." });
        }

        var response = await cuiaAi.AskAsync(new CuiaAiQuery(
            Prompt: req.Prompt,
            City: req.City,
            Occasion: req.Occasion,
            Dietary: req.Dietary,
            Budget: req.Budget
        ), ct);

        return Ok(response);
    }

    [HttpGet("/api/cuia-ia/dica-restaurante/{id:long}")]
    public async Task<IActionResult> RestaurantAdvice(long id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest();
        var advice = await cuiaAi.GetRestaurantAdviceAsync(id, ct);
        return Ok(new { advice });
    }

    [HttpGet("/api/cuia-ia/dica-receita/{slug}")]
    public async Task<IActionResult> RecipeSubstitutions(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 100) return BadRequest();
        var tips = await cuiaAi.GetRecipeSubstitutionsAsync(slug, ct);
        return Ok(new { tips });
    }
}
