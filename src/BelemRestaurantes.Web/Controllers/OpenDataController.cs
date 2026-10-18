using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class OpenDataController(IOpenDataCache cache) : Controller
{
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
}
