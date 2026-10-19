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
    ISearchGapWriter searchGaps,
    IGeoMunicipalityRepository? geoRepository = null,
    IGeoDistrictLocalityRepository? localityRepository = null) : Controller
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
        [FromQuery] string? sort = null,
        [FromQuery] bool photosOnly = false,
        [FromQuery] double? lat = null,
        [FromQuery] double? lng = null,
        [FromQuery] double radius = 10,
        [FromQuery] string? tier = null,
        CancellationToken ct = default)
    {
        var searchQuery = new RestaurantSearchQuery(
            Term: q,
            City: city,
            Category: category,
            Neighborhood: neighborhood,
            Page: Math.Max(1, page),
            PageSize: 24, Sort: sort, PhotosOnly: photosOnly, Latitude: lat, Longitude: lng, RadiusKm: radius, Tier: tier);

        var result = await restaurantRepository.SearchAsync(searchQuery, ct);
        if (result.TotalCount == 0 && !string.IsNullOrWhiteSpace(q))
        {
            try { await searchGaps.RecordAsync(q, city, 0, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* a busca pública não depende do registro da lacuna */ }
        }
        var filterOptions = await searchService.GetFilterOptionsAsync(ct);

        ViewBag.SearchTerm = q;
        ViewBag.SelectedCity = city;
        ViewBag.SelectedCategory = category;
        ViewBag.SelectedNeighborhood = neighborhood;
        ViewBag.FilterOptions = filterOptions;
        ViewData["Query"] = searchQuery;

        return View(result);
    }

    [HttpGet("cidades")]
    public async Task<IActionResult> Cities(CancellationToken ct, [FromQuery] string? q = null, [FromQuery] string? region = null)
    {
        var municipalities = geoRepository != null
            ? await geoRepository.ListAllAsync(ct)
            : Array.Empty<BelemRestaurantes.Domain.Entities.GeoMunicipality>();

        var localities = localityRepository != null
            ? await localityRepository.ListTouristHighlightsAsync(ct)
            : Array.Empty<BelemRestaurantes.Domain.Entities.GeoDistrictLocality>();

        ViewBag.TouristLocalities = localities;
        ViewData["AtlasQuery"] = q;
        ViewData["AtlasRegion"] = region;
        ViewData["CityCounts"] = await restaurantRepository.CountPublishedByCityAsync(ct);
        if(!string.IsNullOrWhiteSpace(q)) municipalities=municipalities.Where(c=>BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(c.Name)
            .Contains(BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(q.Trim()[..Math.Min(q.Trim().Length,120)]),StringComparison.OrdinalIgnoreCase)).ToArray();
        if(!string.IsNullOrWhiteSpace(region)) municipalities=municipalities.Where(c=>c.Mesoregion==region).ToArray();
        return View(municipalities);
    }

    [HttpGet("localidade/{slug}")]
    public async Task<IActionResult> LocalityPage(string slug, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var normalizedSlug = slug.Replace("-", " ").Trim();
        if (normalizedSlug.Length == 0) return NotFound();
        var locality = localityRepository != null ? await localityRepository.GetByNameAsync(normalizedSlug, ct) : null;
        var name = locality?.Name ?? char.ToUpper(normalizedSlug[0]) + normalizedSlug[1..];

        var searchQuery = new RestaurantSearchQuery(
            Neighborhood: name,
            Page: Math.Max(1, page),
            PageSize: 24);

        var result = await restaurantRepository.SearchAsync(searchQuery, ct);
        var filterOptions = await searchService.GetFilterOptionsAsync(ct);

        ViewBag.SelectedNeighborhood = name;
        ViewBag.DistrictLocality = locality;
        ViewBag.FilterOptions = filterOptions;

        return View("Index", result);
    }

    [HttpGet("cidade/{slug}")]
    public async Task<IActionResult> CityPage(string slug, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var normalizedSlug = slug.Replace("-", " ").Trim();
        if (normalizedSlug.Length == 0) return NotFound();
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
