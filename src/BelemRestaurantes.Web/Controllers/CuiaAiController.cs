using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class CuiaAiController(ICuiaAiService cuiaAi, IRestaurantReadRepository restaurantRepo, ISearchArchive? searchArchive = null) : Controller
{
    [HttpGet("/cuia-ia")]
    [EnableRateLimiting("cuia")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? city, [FromQuery] string? occasion, [FromQuery] string? dietary, CancellationToken ct, [FromQuery] bool searchWeb = true)
    {
        ViewData["Title"] = "Cuia IA — Seu guia de sabores do Pará";
        ViewData["MetaDescription"] = "Conte o que você procura. A Cuia ajuda a encontrar restaurantes, pratos e experiências paraenses.";

        var cities = await restaurantRepo.ListCitiesAsync(ct);
        ViewData["Cities"] = cities;
        ViewData["InitialPrompt"] = q ?? "";
        ViewData["InitialCity"] = city ?? "";
        ViewData["SearchWeb"] = searchWeb;

        CuiaAiResponse? initialResponse = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            initialResponse = await AskMeasured(new CuiaAiQuery(q, city, occasion, dietary, SearchWeb: searchWeb), ct);
        }

        return View(initialResponse);
    }

    public sealed record AskRequest(string Prompt, string? City, string? Occasion, string? Dietary, string? Budget, bool SearchWeb = true,
        IReadOnlyList<string>? PreviousQuestions = null, IReadOnlyList<long>? PlaceIds = null);

    [HttpPost("/api/cuia-ia/ask")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cuia")]
    [RequestSizeLimit(8192)]
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
        if ((req.PreviousQuestions?.Count ?? 0) > 3 || (req.PlaceIds?.Count ?? 0) > 4 ||
            (req.PreviousQuestions?.Any(q => q is null || q.Length > 500) ?? false) ||
            new[] { req.City, req.Occasion, req.Dietary, req.Budget }.Any(s => s?.Length > 100))
            return ApiProblems.Result(HttpContext, 400, "context_too_long", "Reduza os filtros ou reinicie a conversa nesta aba.", false);

        var response = await AskMeasured(new CuiaAiQuery(
            Prompt: req.Prompt,
            City: req.City,
            Occasion: req.Occasion,
            Dietary: req.Dietary,
            Budget: req.Budget,
            SearchWeb: req.SearchWeb,
            PreviousQuestions: req.PreviousQuestions,
            PlaceIds: req.PlaceIds
        ), ct);

        return Ok(response);
    }

    private async Task<CuiaAiResponse> AskMeasured(CuiaAiQuery query, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if(searchArchive is not null)await SearchTopicRecorder.RecordAsync(HttpContext,searchArchive,query.Prompt,ct);
        try
        {
            var response = await cuiaAi.AskAsync(query with { SessionKey = CuiaSessionIdentity.GetOrCreate(HttpContext) }, ct);
            CuiaAskMetrics.Record(response.AnswerKind, System.Diagnostics.Stopwatch.GetElapsedTime(started));
            return response with{EngineUsed="Cuia",AnswerMarkdown=System.Text.RegularExpressions.Regex.Replace(response.AnswerMarkdown,@"\b(Drael|Gemini|Groq|DeepSeek)\b","Cuia",System.Text.RegularExpressions.RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100))};
        }
        catch
        {
            CuiaAskMetrics.RecordFailure();
            throw;
        }
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
            service = "Cuia",
            cloudConfigurationReady = status.Providers.Any(p => p.EnabledForCuia),
            liveProbePerformed = false,
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
