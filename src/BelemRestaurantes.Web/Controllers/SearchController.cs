using System;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class SearchController(
    ISearchService searchService,
    IRestaurantReadRepository restaurantRepository) : Controller
{
    [HttpGet("api/search/autocomplete")]
    public async Task<IActionResult> Autocomplete([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Json(Array.Empty<AutocompleteResult>());
        }

        var results = await searchService.AutocompleteAsync(q, ct);
        return Json(results);
    }

    [HttpGet("busca")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? city,
        [FromQuery] string? category,
        [FromQuery] string? neighborhood,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var searchQuery = new RestaurantSearchQuery(
            Term: q,
            City: city,
            Category: category,
            Neighborhood: neighborhood,
            Page: Math.Max(1, page),
            PageSize: 24);

        var result = await restaurantRepository.SearchAsync(searchQuery, ct);
        var filterOptions = await searchService.GetFilterOptionsAsync(ct);

        ViewBag.SearchTerm = q;
        ViewBag.SelectedCity = city;
        ViewBag.SelectedCategory = category;
        ViewBag.SelectedNeighborhood = neighborhood;
        ViewBag.FilterOptions = filterOptions;

        return View(result);
    }
}
