using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class MapController(IRestaurantReadRepository restaurants) : Controller
{
    [HttpGet("/mapa")]
    public async Task<IActionResult> Index(string? city, string? q, CancellationToken ct)
    {
        var all = await restaurants.ListPublishedMapAsync(ct);
        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (term is { Length: > 80 }) term = term[..80];
        var selectedCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        var filtered = all.Where(place =>
            (selectedCity is null || string.Equals(place.City, selectedCity, StringComparison.OrdinalIgnoreCase))
            && (term is null
                || place.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (place.Neighborhood?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (place.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
        var cities = all.Select(place => place.City).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (selectedCity is not null && !cities.Contains(selectedCity, StringComparer.OrdinalIgnoreCase))
            selectedCity = null;
        var page = new CatalogMapPage(
            filtered,
            CatalogMapLayout.Plot(filtered),
            cities,
            all.Count,
            all.Count(place => place.CatalogTier == "guia"),
            selectedCity,
            term);
        return View(page);
    }
}
