using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous, EnableRateLimiting("public")]
public sealed class DiscoveryController(IRestaurantReadRepository restaurants, IParaRecipes recipes, IFavoriteRepository favorites) : Controller
{
    [HttpGet("/descobrir")]
    public async Task<IActionResult> Index(string? city, string? moment, string? dish, CancellationToken ct)
    {
        var safeCity = ParaOfficialCatalog.Municipalities.FirstOrDefault(c => string.Equals(c.Name, city, StringComparison.OrdinalIgnoreCase))?.Name;
        var safeMoment = moment is "almoco" or "cafe" or "jantar" or "familia" or "a-dois" or "passeio" ? moment : "almoco";
        var entry = ParaTasteCatalog.Dishes.FirstOrDefault(d => d.Slug == dish);
        var term = entry?.Aliases.FirstOrDefault() ?? (safeMoment == "cafe" ? "Café" : null);
        var places = await restaurants.SearchAsync(new(Term: term, City: safeCity, PageSize: 6), ct);
        var recipePage = await recipes.ListAsync(new(Query: entry?.Title), false, ct);
        return View(new DiscoveryPage(safeCity, safeMoment, entry?.Slug, places.Items, recipePage.Items.Take(3).ToArray()));
    }

    [HttpGet("/meu-passeio")]
    public async Task<IActionResult> Saved(CancellationToken ct)
    {
        var member = await HttpContext.AuthenticateAsync(MemberAuth.Scheme);
        var places = new List<RestaurantView>();
        if (member.Succeeded && member.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) is {} subject)
            foreach (var id in (await favorites.ListAsync(subject, ct)).Take(20))
                if (await restaurants.GetPublishedAsync(id, ct) is {} place) places.Add(place);
        return View(places);
    }

    [HttpGet("/api/descobrir/guardados")]
    public async Task<IActionResult> SavedPlaces(string? ids, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private,no-store";
        var parts = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if ((ids?.Length ?? 0) > 500 || parts.Length > 20 || parts.Any(p => !long.TryParse(p, out var id) || id <= 0))
            return BadRequest(new { error = "Informe até vinte identificadores válidos." });
        var places = new List<object>();
        foreach (var id in parts.Select(long.Parse).Distinct())
            if (await restaurants.GetPublishedAsync(id, ct) is {} place)
                places.Add(new { place.Id, place.Name, place.City, place.CatalogTier });
        return Ok(new { items = places });
    }
}
