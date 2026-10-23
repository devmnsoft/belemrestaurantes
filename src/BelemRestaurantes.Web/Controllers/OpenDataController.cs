using System.Globalization;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class OpenDataController(IOpenDataCache cache, IRestaurantReadRepository restaurants) : Controller
{
    // Limite de proteção do export: acima disso o payload GeoJSON deixaria de ser amigável sem paginação.
    private const int MaxGeoJsonFeatures = 250_000;

    [HttpGet("/dados-abertos")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Atlas aberto do Pará — dados e exportações";
        ViewData["MetaDescription"] = "Catálogo público de estabelecimentos publicados: números honestos da cobertura, metodologia da verificação e exportações GeoJSON e CSV para reuso.";
        var summary = await restaurants.GetOpenDataSummaryAsync(ct);
        return View(summary);
    }

    [HttpGet("dados-abertos/openstreetmap")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var snapshots = await cache.ListAsync(ct);
        return Json(new
        {
            attribution = "© OpenStreetMap contributors", license = "ODbL-1.0",
            licenseUrl = "https://www.openstreetmap.org/copyright",
            description = "Snapshots da fonte aberta. Não incluem avaliações, dados de usuários ou dados privados de parceiros.",
            snapshots = snapshots.Select(s => new { s.City, s.FetchedAt, data = JsonSerializer.Deserialize<JsonElement>(s.Payload) })
        });
    }

    /// <summary>GeoJSON dos publicados com coordenada válida. Campos públicos da ficha; confirmação operacional sempre na página do estabelecimento.</summary>
    [HttpGet("/api/dados-abertos/geojson")]
    public async Task<IActionResult> GeoJson(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public,max-age=30";
        Response.ContentType = "application/geo+json; charset=utf-8";
        var summary = await restaurants.GetOpenDataSummaryAsync(ct);
        var features = new List<object>(Math.Min(summary.WithCoordinates, MaxGeoJsonFeatures));
        var truncated = false;
        for (long after = 0; ; )
        {
            var rows = await restaurants.GetOpenDataRowsAsync(after, 1000, ct);
            if (rows.Count == 0) break;
            foreach (var row in rows)
            {
                if (row.Latitude is {} lat && row.Longitude is {} lng && lat is > -90 and < 90 && lng is > -180 and < 180)
                {
                    features.Add(new
                    {
                        type = "Feature",
                        geometry = new { type = "Point", coordinates = new[] { lng, lat } },
                        properties = new
                        {
                            id = row.Id, name = row.Name, city = row.City, neighborhood = row.Neighborhood,
                            category = row.Category, address = row.Address,
                            tier = row.CatalogTier, business_status = row.BusinessStatus,
                            opening_hours = row.OpeningHours, phone = row.Phone, website = row.Website
                        }
                    });
                    if (features.Count >= MaxGeoJsonFeatures) { truncated = true; break; }
                }
            }
            if (truncated || rows.Count < 1000) break;
            after = rows[^1].Id;
        }
        var payload = new
        {
            type = "FeatureCollection",
            metadata = new
            {
                generated_at_utc = summary.GeneratedAtUtc,
                published_total = summary.PublishedTotal,
                with_coordinates = summary.WithCoordinates,
                truncated,
                attribution = "© OpenStreetMap contributors (origem das coordenadas)",
                license = "ODbL-1.0",
                license_url = "https://www.openstreetmap.org/copyright",
                description = "Catálogo público de estabelecimentos publicados pelo portal Belém Restaurantes. Confira funcionamento na ficha antes de visitar."
            },
            features
        };
        await Response.WriteAsync(JsonSerializer.Serialize(payload), ct);
        return new EmptyResult();
    }

    /// <summary>CSV de todo o catálogo publicado (somente campos já públicos). BOM UTF-8 para abertura correta no Excel pt-BR.</summary>
    [HttpGet("/api/dados-abertos.csv")]
    public async Task<IActionResult> Csv(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = "attachment; filename=\"belem-restaurantes-catalogo.csv\"";
        var summary = await restaurants.GetOpenDataSummaryAsync(ct);
        Response.Headers["X-Generated-At"] = summary.GeneratedAtUtc.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
        await Response.WriteAsync("\uFEFF", ct);
        await Response.WriteAsync("id,name,city,neighborhood,category,address,latitude,longitude,tier,business_status,opening_hours,phone,website\r\n", ct);
        for (long after = 0; ; )
        {
            var rows = await restaurants.GetOpenDataRowsAsync(after, 1000, ct);
            if (rows.Count == 0) break;
            var buffer = new StringBuilder(rows.Count * 160);
            foreach (var row in rows)
            {
                buffer.Append(row.Id).Append(',')
                    .Append(CsvField(row.Name)).Append(',')
                    .Append(CsvField(row.City)).Append(',')
                    .Append(CsvField(row.Neighborhood)).Append(',')
                    .Append(CsvField(row.Category)).Append(',')
                    .Append(CsvField(row.Address)).Append(',');
                buffer.Append(row.Latitude is {} lat ? lat.ToString("0.0000000", CultureInfo.InvariantCulture) : "").Append(',');
                buffer.Append(row.Longitude is {} lng ? lng.ToString("0.0000000", CultureInfo.InvariantCulture) : "").Append(',')
                    .Append(CsvField(row.CatalogTier)).Append(',')
                    .Append(CsvField(row.BusinessStatus)).Append(',')
                    .Append(CsvField(row.OpeningHours)).Append(',')
                    .Append(CsvField(row.Phone)).Append(',')
                    .Append(CsvField(row.Website))
                    .Append("\r\n");
            }
            await Response.WriteAsync(buffer.ToString(), ct);
            if (rows.Count < 1000) break;
            after = rows[^1].Id;
        }
        return new EmptyResult();
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = needsQuotes ? value.Replace("\"", "\"\"", StringComparison.Ordinal) : value;
        return needsQuotes ? "\"" + escaped + "\"" : escaped;
    }
}
