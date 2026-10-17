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
    IRestaurantReadRepository restaurantRepository,
    IGeoMunicipalityRepository? geoRepository = null) : Controller
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

    [HttpGet("cidades")]
    public async Task<IActionResult> Cities(CancellationToken ct)
    {
        var municipalities = geoRepository != null
            ? await geoRepository.ListAllAsync(ct)
            : Array.Empty<BelemRestaurantes.Domain.Entities.GeoMunicipality>();

        return View(municipalities);
    }

    [HttpGet("cidade/{slug}")]
    public async Task<IActionResult> CityPage(string slug, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var normalizedSlug = slug.Replace("-", " ").Trim();
        var geo = geoRepository != null ? await geoRepository.GetByNameAsync(normalizedSlug, ct) : null;
        var cityName = geo?.Name ?? char.ToUpper(normalizedSlug[0]) + normalizedSlug[1..];

        var searchQuery = new RestaurantSearchQuery(
            City: cityName,
            Page: Math.Max(1, page),
            PageSize: 24);

        var result = await restaurantRepository.SearchAsync(searchQuery, ct);
        var filterOptions = await searchService.GetFilterOptionsAsync(ct);

        ViewBag.SelectedCity = cityName;
        ViewBag.GeoMunicipality = geo;
        ViewBag.FilterOptions = filterOptions;

        return View("Index", result);
    }

    [HttpGet("categoria/{slug}")]
    public async Task<IActionResult> CategoryPage(string slug, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var categoryName = slug.Replace("-", " ").Trim();

        var searchQuery = new RestaurantSearchQuery(
            Category: categoryName,
            Page: Math.Max(1, page),
            PageSize: 24);

        var result = await restaurantRepository.SearchAsync(searchQuery, ct);
        var filterOptions = await searchService.GetFilterOptionsAsync(ct);

        ViewBag.SelectedCategory = categoryName;
        ViewBag.FilterOptions = filterOptions;

        return View("Index", result);
    }
}
