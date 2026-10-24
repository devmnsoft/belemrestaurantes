using System;
using System.Globalization;
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
        if (lat is null && lng is null && sort == "nearby" && TryReadNearby(Request.Cookies["br_near"], out var cookieLat, out var cookieLng, out var cookieRadius))
        {
            lat = cookieLat; lng = cookieLng; radius = cookieRadius;
        }
        q = q is null ? null : BelemRestaurantes.Application.Services.CulinaryQuery.Correct(q);
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

    [HttpPost("busca/perto")]
    [ValidateAntiForgeryToken]
    public IActionResult Nearby(double lat, double lng, double radius = 10)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
            return Redirect("/busca");
        var safeRadius = Math.Clamp(double.IsFinite(radius) ? radius : 10, 1, 100);
        var coarseLat = Math.Round(lat, 2, MidpointRounding.AwayFromZero);
        var coarseLng = Math.Round(lng, 2, MidpointRounding.AwayFromZero);
        Response.Cookies.Append("br_near", string.Create(CultureInfo.InvariantCulture, $"{coarseLat:0.00}|{coarseLng:0.00}|{safeRadius:0}"), new CookieOptions
        {
            HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, MaxAge = TimeSpan.FromMinutes(30)
        });
        return Redirect("/busca?sort=nearby&radius=" + safeRadius.ToString("0", CultureInfo.InvariantCulture));
    }

    private static bool TryReadNearby(string? cookie, out double lat, out double lng, out double radius)
    {
        lat = lng = 0; radius = 10;
        var parts = cookie?.Split('|');
        return parts is { Length: 3 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lng)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius)
            && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
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

        // Sprint 79 — quando a categoria existe no catálogo, exibe o rótulo guardado e não o slug
        // decodificado em minúsculas: a busca é insensível a maiúsculas/acentos, então trocar não
        // muda o resultado, apenas o nome aparece como o próprio catálogo o escreveu.
        var canonicalLabel = filterOptions.Categories.FirstOrDefault(c => string.Equals(BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(c), BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(categoryName), StringComparison.OrdinalIgnoreCase));
        if (canonicalLabel is not null)
        {
            categoryName = canonicalLabel;
        }

        ViewBag.SelectedCategory = categoryName;
        ViewBag.FilterOptions = filterOptions;

        return View("Index", result);
    }
}
