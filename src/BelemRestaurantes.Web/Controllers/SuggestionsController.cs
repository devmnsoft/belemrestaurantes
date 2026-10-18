using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
[Route("sugerir")]
public sealed class SuggestionsController(ISuggestionRepository suggestions, IRestaurantReadRepository catalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long? restaurantId, CancellationToken ct)
    {
        var input = new RestaurantSuggestionInput();
        if (restaurantId is { } id)
        {
            var restaurant = await catalog.GetPublishedAsync(id, ct);
            if (restaurant is null) return NotFound();
            input.RestaurantId = id; input.Name = restaurant.Name; input.City = restaurant.City; input.Website = restaurant.Website;
        }
        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("suggestions")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Submit(RestaurantSuggestionInput input, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(input.ContactTrap)) return BadRequest();
        var city = ParaOfficialCatalog.Municipalities.FirstOrDefault(c => c.NormalizedName == NameNormalizer.Normalize(input.City));
        if (city is null) ModelState.AddModelError(nameof(input.City), "Escolha um município do Pará.");
        if (!string.IsNullOrWhiteSpace(input.Website) && !SocialLinkRules.TryValidate(input.Website, out _))
            ModelState.AddModelError(nameof(input.Website), "Use um link HTTPS do site ou perfil oficial.");
        if (input.RestaurantId is { } id && await catalog.GetPublishedAsync(id, ct) is null) return NotFound();
        if (!ModelState.IsValid) return View("Index", input);
        await suggestions.AddAsync(input.Name.Trim(), city!.Name, input.Website?.Trim(), input.Notes, ct, input.RestaurantId);
        TempData["SuggestionReceived"] = true;
        return RedirectToAction(nameof(Index));
    }
}
