using System.Security.Claims;
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
    private string? ViewerUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var establishment = await repo.GetPublishedAsync(id, ct);

        if (establishment is null)
        {
            return NotFound();
        }

        var cuiaBadges = await cuiaService.GetBadgesForRestaurantAsync(id, ct);
        var gallery = await photoService.GetGalleryForRestaurantAsync(id, ct);
        var communityExperiences = await experienceService.ListPublicByRestaurantAsync(id, 1, 30, ViewerUserId(), ct);
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