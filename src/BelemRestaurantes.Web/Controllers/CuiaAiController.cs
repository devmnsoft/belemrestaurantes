using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class CuiaAiController(ICuiaAiService cuiaAi, IRestaurantReadRepository restaurantRepo) : Controller
{
    [HttpGet("/cuia-ia")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? city, [FromQuery] string? occasion, [FromQuery] string? dietary, CancellationToken ct)
    {
        ViewData["Title"] = "Cuia IA — Seu guia de sabores do Pará";
        ViewData["MetaDescription"] = "Conte o que você procura. A Cuia ajuda a encontrar restaurantes, pratos e experiências paraenses.";

        var cities = await restaurantRepo.ListCitiesAsync(ct);
        ViewData["Cities"] = cities;
        ViewData["InitialPrompt"] = q ?? "";
        ViewData["InitialCity"] = city ?? "";

        CuiaAiResponse? initialResponse = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            initialResponse = await cuiaAi.AskAsync(new CuiaAiQuery(q, city, occasion, dietary), ct);
        }

        return View(initialResponse);
    }

    public sealed record AskRequest(string Prompt, string? City, string? Occasion, string? Dietary, string? Budget);

    [HttpPost("/api/cuia-ia/ask")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cuia")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Prompt))
        {
            return ApiProblems.Result(HttpContext, StatusCodes.Status400BadRequest, "prompt_required", "Por favor, digite sua dúvida ou preferência gastronômica.", false);
        }

        if (req.Prompt.Length > 500)
        {
            return ApiProblems.Result(HttpContext, StatusCodes.Status400BadRequest, "prompt_too_long", "A consulta pode ter no máximo 500 caracteres.", false);
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

    [HttpGet("/api/cuia-ia/status")]
    [AllowAnonymous]
    public IActionResult Status()
    {
        var status = cuiaAi.GetStatus();
        return Ok(new
        {
            status = "ok",
            providers = status.Providers,
            promptInjectionGuard = status.PromptInjectionGuard,
            rateLimitPolicy = status.RateLimitPolicy,
            costTracking = "enabled"
        });
    }

    [HttpGet("/api/cuia-ia/dica-receita/{slug}")]
    public async Task<IActionResult> RecipeSubstitutions(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 100) return BadRequest();
        var tips = await cuiaAi.GetRecipeSubstitutionsAsync(slug, ct);
        return Ok(new { tips });
    }
}
