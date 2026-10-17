using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BelemRestaurantes.Web.Controllers;

[Route("")]
public sealed class ExperienceController(
    IExperienceService experienceService,
    IPhotoStorageService photoService,
    IRestaurantReadRepository restaurantRepo,
    IStringLocalizer<SharedResource> text) : Controller
{
    private string? ViewerUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string Subject() => ViewerUserId() ?? throw new InvalidOperationException("Usuário não autenticado.");
    private string AuthorName() => User.Identity?.Name ?? "Visitante";

    [HttpGet("community")]
    [HttpGet("feed")]
    [AllowAnonymous]
    public async Task<IActionResult> Feed(string? sort = "recent", int page = 1, CancellationToken ct = default)
    {
        var items = await experienceService.ListPublicFeedAsync(page, 20, sort, ViewerUserId(), ct);
        ViewData["Sort"] = sort ?? "recent";
        ViewData["Page"] = page;
        return View("~/Views/Experience/Feed.cshtml", items);
    }

    [HttpGet("experience/{id:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Details(long id, string? token = null, CancellationToken ct = default)
    {
        var exp = await experienceService.GetByIdAsync(id, ViewerUserId(), token, ct);
        if (exp is null)
        {
            return NotFound();
        }

        var comments = await experienceService.ListCommentsAsync(id, ct);
        ViewData["Comments"] = comments;
        ViewData["UnlistedToken"] = token;
        return View("~/Views/Experience/Details.cshtml", exp);
    }

    [HttpGet("experience/create")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    public async Task<IActionResult> Create(long restaurantId, CancellationToken ct)
    {
        var restaurant = await restaurantRepo.GetPublishedAsync(restaurantId, ct);
        if (restaurant is null)
        {
            return NotFound();
        }

        ViewData["Restaurant"] = restaurant;
        return View("~/Views/Experience/Create.cshtml", new ExperienceInputModel { RestaurantId = restaurantId });
    }

    [HttpPost("experience/create")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [RequestSizeLimit(35 * 1024 * 1024)] // 35 MB total
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> CreateSubmit([FromForm] ExperienceInputModel input, [FromForm] List<IFormFile>? photos, CancellationToken ct)
    {
        var restaurant = await restaurantRepo.GetPublishedAsync(input.RestaurantId, ct);
        if (restaurant is null) return NotFound();

        if (input.Stars is < 1 or > 5 || input.ServiceStars is < 1 or > 5 ||
            input.FlavorStars is < 1 or > 5 || input.AmbianceStars is < 1 or > 5 ||
            input.CostBenefitStars is < 1 or > 5)
        {
            ModelState.AddModelError("", text["ExperienceInvalidScores"]);
            ViewData["Restaurant"] = restaurant;
            return View("~/Views/Experience/Create.cshtml", input);
        }

        var expId = await experienceService.CreateAsync(new CreateExperienceInput(
            input.RestaurantId,
            Subject(),
            AuthorName(),
            input.Stars,
            input.ServiceStars,
            input.FlavorStars,
            input.AmbianceStars,
            input.CostBenefitStars,
            input.ReviewText,
            input.DishesConsumed,
            input.PricePaid,
            input.Visibility ?? "public"
        ), ct);

        // Upload seguro das fotografias
        if (photos is { Count: > 0 })
        {
            var validPhotos = photos.Take(PhotoRules.MaxPhotosPerExperience);
            foreach (var file in validPhotos)
            {
                if (file.Length is > 0 and <= PhotoRules.MaxFileSizeBytes && PhotoRules.IsValidExtension(file.FileName))
                {
                    try
                    {
                        await using var stream = file.OpenReadStream();
                        await photoService.ProcessAndSaveAsync(stream, file.FileName, file.ContentType, Subject(), input.RestaurantId, expId, null, ct);
                    }
                    catch
                    {
                        // Se arquivo for inválido ou corrompido, continua com os demais
                    }
                }
            }
        }

        return RedirectToAction("Details", new { id = expId });
    }

    [HttpGet("experience/edit/{id:long}")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    public async Task<IActionResult> Edit(long id, CancellationToken ct)
    {
        var exp = await experienceService.GetByIdAsync(id, Subject(), null, ct);
        if (exp is null || exp.UserId != Subject())
        {
            return Forbid();
        }

        var model = new ExperienceInputModel
        {
            Id = exp.Id,
            RestaurantId = exp.RestaurantId,
            Stars = exp.Stars,
            ServiceStars = exp.ServiceStars,
            FlavorStars = exp.FlavorStars,
            AmbianceStars = exp.AmbianceStars,
            CostBenefitStars = exp.CostBenefitStars,
            ReviewText = exp.ReviewText,
            DishesConsumed = exp.DishesConsumed,
            PricePaid = exp.PricePaid,
            Visibility = exp.Visibility
        };

        var restaurant = await restaurantRepo.GetPublishedAsync(exp.RestaurantId, ct);
        ViewData["Restaurant"] = restaurant;
        ViewData["Photos"] = exp.Photos;

        return View("~/Views/Experience/Edit.cshtml", model);
    }

    [HttpPost("experience/edit/{id:long}")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSubmit(long id, [FromForm] ExperienceInputModel input, CancellationToken ct)
    {
        var updated = await experienceService.UpdateAsync(new UpdateExperienceInput(
            id,
            Subject(),
            input.Stars,
            input.ServiceStars,
            input.FlavorStars,
            input.AmbianceStars,
            input.CostBenefitStars,
            input.ReviewText,
            input.DishesConsumed,
            input.PricePaid,
            input.Visibility ?? "public"
        ), ct);

        if (!updated)
        {
            return Forbid();
        }

        return RedirectToAction("Details", new { id });
    }

    [HttpPost("experience/delete/{id:long}")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var deleted = await experienceService.DeleteAsync(id, Subject(), isAdmin: false, ct);
        if (!deleted)
        {
            return Forbid();
        }

        return Redirect("/account");
    }

    [HttpPost("experience/{id:long}/like")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLike(long id, CancellationToken ct)
    {
        var liked = await experienceService.ToggleLikeAsync(id, Subject(), ct);
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Ok(new { liked });
        }

        return Redirect(Request.Headers.Referer.ToString() ?? $"/experience/{id}");
    }

    [HttpPost("experience/{id:long}/comment")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(long id, [FromForm] string commentText, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(commentText))
        {
            try
            {
                await experienceService.AddCommentAsync(id, Subject(), AuthorName(), commentText, ct);
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
        }

        return Redirect($"/experience/{id}#comentarios");
    }

    [HttpPost("experience/{id:long}/report")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(long id, [FromForm] string reason, [FromForm] string? notes, CancellationToken ct)
    {
        await experienceService.ReportAsync(Subject(), "experience", id, reason, notes, ct);
        TempData["ReportNotice"] = text["ReportReceived"].Value;
        return Redirect($"/experience/{id}");
    }

    [HttpPost("experience/{id:long}/appeal")]
    [Authorize(AuthenticationSchemes = MemberAuth.Scheme, Policy = MemberAuth.Policy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Appeal(long id, [FromForm] string appealText, CancellationToken ct)
    {
        var appealed = await experienceService.SubmitAppealAsync(id, Subject(), appealText, ct);
        if (!appealed)
        {
            TempData["ErrorMessage"] = text["AppealFailed"].Value;
        }
        else
        {
            TempData["SuccessMessage"] = text["AppealSubmitted"].Value;
        }

        return Redirect($"/experience/{id}");
    }
}
