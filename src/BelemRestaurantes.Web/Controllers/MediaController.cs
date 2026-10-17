using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Route("media")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class MediaController(IPhotoStorageService photoService) : Controller
{
    private string? ViewerUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("photo/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Photo(string fileName, CancellationToken ct)
    {
        var result = await photoService.GetPhotoFileAsync(fileName, ViewerUserId(), isThumb: false, ct);
        if (result is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(result.Value.Stream, result.Value.ContentType);
    }

    [HttpGet("thumb/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Thumb(string fileName, CancellationToken ct)
    {
        var result = await photoService.GetPhotoFileAsync(fileName, ViewerUserId(), isThumb: true, ct);
        if (result is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(result.Value.Stream, result.Value.ContentType);
    }
}
