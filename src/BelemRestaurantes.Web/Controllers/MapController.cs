using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Globalization;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class MapController(IRestaurantReadRepository restaurants, IMemoryCache cache, IConfiguration configuration) : Controller
{
    [HttpGet("/mapa")]
    public async Task<IActionResult> Index(string? city, string? q, string? category, string? neighborhood, string? tier, long after, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var summary = await cache.GetOrCreateAsync("map:summary", async entry => {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await restaurants.GetMapSummaryAsync(ct);
        }) ?? new MapSummary(0, 0, []);
        var query = new MapQuery(MapQuery.Clean(city), MapQuery.Clean(q), MapQuery.Clean(category), MapQuery.Clean(neighborhood), tier, After: after, Limit: 24);
        var result = await restaurants.SearchMapAsync(query, ct);
        Response.Headers["Server-Timing"] = "catalog;dur=" + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        ViewData["MapQuery"] = query;
        ViewData["MapNext"] = result.Next;
        // Only same-origin licensed tile gateways: no public tile server default or secret exposed in HTML.
        var tiles = configuration["Maps:TileUrl"];
        ViewData["MapTileUrl"] = tiles is not null && tiles.StartsWith("/maps/tiles/", StringComparison.Ordinal) && !tiles.Contains("..") ? tiles : "";
        ViewData["MapAttribution"] = configuration["Maps:Attribution"] ?? "";
        return View(new CatalogMapPage(result.Points, CatalogMapLayout.Plot(result.Points), summary.Cities, summary.Total, summary.Guides, query.City, query.Term));
    }

    [HttpGet("/api/mapa/pontos")]
    public async Task<IActionResult> Points(string? bbox, string? city, string? q, string? category, string? neighborhood, string? tier, long after, int limit = 100, CancellationToken ct = default)
    {
        if (!MapBounds.TryParse(bbox, out var bounds) || after < 0 || limit is < 1 or > 200 || (tier is not null && tier is not ("ficha" or "guia")))
            return BadRequest(new { error = "Informe bbox oeste,sul,leste,norte válido; limite entre 1 e 200 e camada ficha ou guia." });
        var query = new MapQuery(MapQuery.Clean(city), MapQuery.Clean(q), MapQuery.Clean(category), MapQuery.Clean(neighborhood), tier, bounds, after, limit);
        // Short HTTP cache; arbitrary geographic queries never grow an unbounded server cache.
        Response.Headers.CacheControl = "public,max-age=15";
        var watch = Stopwatch.StartNew();
        var result = await restaurants.SearchMapAsync(query, ct);
        Response.Headers["Server-Timing"] = "sql;dur=" + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        return Ok(result);
    }
}
