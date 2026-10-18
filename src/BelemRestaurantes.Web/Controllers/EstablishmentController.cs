using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Route("establishment")]
[AllowAnonymous]
public sealed class EstablishmentController(
    IRestaurantReadRepository repo,
    IExperienceService experienceService,
    IPhotoStorageService photoService,
    ICuiaService cuiaService,
    IRestaurantMediaRepository? mediaRepo = null,
    IRestaurantAttributeHistoryRepository? historyRepo = null) : Controller
{
    [HttpGet("{id:long}")]
    [HttpGet("/estabelecimento/{id:long}")]
    public async Task<IActionResult> Details(long id, [FromServices] IFavoriteRepository favorites, CancellationToken ct)
    {
        var establishment = await repo.GetPublishedAsync(id, ct);

        if (establishment is null)
        {
            return NotFound();
        }

        var member = await HttpContext.AuthenticateAsync(MemberAuth.Scheme);
        var viewerId = member.Succeeded ? member.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        Response.Headers["Cache-Control"] = "private, no-store";
        ViewData["IsFavorite"] = viewerId is not null && await favorites.IsFavoriteAsync(viewerId, id, ct);
        var cuiaBadges = await cuiaService.GetBadgesForRestaurantAsync(id, ct);
        var gallery = await photoService.GetGalleryForRestaurantAsync(id, ct);
        var communityExperiences = await experienceService.ListPublicByRestaurantAsync(id, 1, 30, viewerId, ct);
        var mediaList = mediaRepo != null ? await mediaRepo.GetMediaByRestaurantAsync(id, onlyApproved: true, ct) : [];
        var attrHistory = historyRepo != null ? await historyRepo.GetHistoryAsync(id, ct) : [];

        ViewData["CuiaBadges"] = cuiaBadges;
        ViewData["Gallery"] = gallery;
        ViewData["Experiences"] = communityExperiences;
        ViewData["MediaList"] = mediaList;
        ViewData["AttributeHistory"] = attrHistory;

        return View(establishment);
    }
}
