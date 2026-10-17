using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
[Route("partner")]
public sealed class PartnerController(
    IPartnerPortalService portalService,
    IPartnerAuthorizationService authService,
    IMonetizationService monetizationService,
    IRestaurantReadRepository restaurantRepository) : Controller
{
    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> AuthorizePartnerAsync(long restaurantId, CancellationToken ct)
    {
        return await authService.IsOwnerOrManagerAsync(restaurantId, CurrentUserId(), ct);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var owned = await portalService.ListOwnedRestaurantsAsync(CurrentUserId(), ct);
        return View(owned);
    }

    [HttpGet("claim/{restaurantId:long}")]
    public async Task<IActionResult> Claim(long restaurantId, CancellationToken ct)
    {
        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var existing = await portalService.GetClaimRequestAsync(restaurantId, CurrentUserId(), ct);
        ViewBag.ExistingClaim = existing;
        ViewBag.Restaurant = restaurant;

        return View(new RestaurantClaimRequest
        {
            RestaurantId = restaurantId,
            RestaurantName = restaurant.Name,
            UserId = CurrentUserId()
        });
    }

    [HttpPost("claim/{restaurantId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaimSubmit(long restaurantId, RestaurantClaimRequest input, CancellationToken ct)
    {
        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        if (string.IsNullOrWhiteSpace(input.BusinessName) || string.IsNullOrWhiteSpace(input.ContactPhone) || string.IsNullOrWhiteSpace(input.VerificationEvidence))
        {
            ModelState.AddModelError("", "Por favor, preencha todos os campos obrigatórios e a comprovação de vínculo com o estabelecimento.");
            ViewBag.Restaurant = restaurant;
            return View("Claim", input);
        }

        input.RestaurantId = restaurantId;
        input.UserId = CurrentUserId();
        await portalService.SubmitClaimRequestAsync(input, ct);

        TempData["SuccessMessage"] = "Solicitação de reivindicação enviada com sucesso! A equipe MNSOFT analisará suas evidências.";
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet("{restaurantId:long}")]
    public async Task<IActionResult> Manage(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var profile = await portalService.GetRestaurantProfileAsync(restaurantId, ct) ?? new RestaurantProfile { RestaurantId = restaurantId };
        var sub = await monetizationService.GetActiveSubscriptionAsync(restaurantId, ct);
        var social = await portalService.ListSocialMediasAsync(restaurantId, ct);

        ViewBag.Restaurant = restaurant;
        ViewBag.Subscription = sub;
        ViewBag.Social = social;

        return View(profile);
    }

    [HttpPost("{restaurantId:long}/profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(long restaurantId, RestaurantProfile profile, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        await portalService.UpdateRestaurantProfileAsync(restaurantId, profile, ct);
        TempData["SuccessMessage"] = "Dados comerciais do estabelecimento atualizados com sucesso!";
        return RedirectToAction(nameof(Manage), new { restaurantId });
    }

    [HttpGet("{restaurantId:long}/schedule")]
    public async Task<IActionResult> Schedule(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var (regular, special) = await portalService.GetSchedulesAsync(restaurantId, ct);
        ViewBag.Restaurant = restaurant;
        ViewBag.SpecialSchedules = special;

        return View(regular);
    }

    [HttpPost("{restaurantId:long}/schedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSchedule(long restaurantId, List<RestaurantSchedule> schedules, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        await portalService.UpdateSchedulesAsync(restaurantId, schedules, Array.Empty<RestaurantSpecialSchedule>(), ct);
        TempData["SuccessMessage"] = "Horários regulares de funcionamento atualizados com sucesso!";
        return RedirectToAction(nameof(Schedule), new { restaurantId });
    }

    [HttpPost("{restaurantId:long}/schedule/special")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSpecialSchedule(long restaurantId, RestaurantSpecialSchedule special, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var (regular, existingSpecial) = await portalService.GetSchedulesAsync(restaurantId, ct);
        var updatedSpecial = existingSpecial.Where(s => s.SpecificDate != special.SpecificDate).ToList();
        special.RestaurantId = restaurantId;
        updatedSpecial.Add(special);

        await portalService.UpdateSchedulesAsync(restaurantId, regular, updatedSpecial, ct);
        TempData["SuccessMessage"] = "Horário especial / feriado cadastrado com sucesso!";
        return RedirectToAction(nameof(Schedule), new { restaurantId });
    }

    [HttpGet("{restaurantId:long}/announcements")]
    public async Task<IActionResult> Announcements(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var list = await portalService.ListAnnouncementsAsync(restaurantId, ct);
        ViewBag.Restaurant = restaurant;
        return View(list);
    }

    [HttpPost("{restaurantId:long}/announcements")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAnnouncement(long restaurantId, RestaurantAnnouncement announcement, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(announcement.Title) || string.IsNullOrWhiteSpace(announcement.Description))
        {
            TempData["ErrorMessage"] = "Título e descrição do evento são obrigatórios.";
            return RedirectToAction(nameof(Announcements), new { restaurantId });
        }

        announcement.RestaurantId = restaurantId;
        await portalService.CreateAnnouncementAsync(announcement, ct);
        TempData["SuccessMessage"] = "Evento ou novidade publicado com sucesso!";
        return RedirectToAction(nameof(Announcements), new { restaurantId });
    }

    [HttpPost("{restaurantId:long}/announcements/{announcementId:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAnnouncement(long restaurantId, long announcementId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        await portalService.DeleteAnnouncementAsync(restaurantId, announcementId, ct);
        TempData["SuccessMessage"] = "Publicação removida com sucesso.";
        return RedirectToAction(nameof(Announcements), new { restaurantId });
    }

    [HttpPost("{restaurantId:long}/reviews/{experienceId:long}/reply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyReview(long restaurantId, long experienceId, string replyText, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(replyText))
        {
            TempData["ErrorMessage"] = "O texto da resposta não pode ser vazio.";
            return RedirectToAction("Details", "Experience", new { id = experienceId });
        }

        await portalService.ReplyToReviewAsync(restaurantId, experienceId, CurrentUserId(), replyText.Trim(), ct);
        TempData["SuccessMessage"] = "Resposta oficial do restaurante publicada com sucesso!";
        return RedirectToAction("Details", "Experience", new { id = experienceId });
    }

    [HttpGet("{restaurantId:long}/stats")]
    public async Task<IActionResult> Stats(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thirtyDaysAgo = today.AddDays(-30);
        var stats = await portalService.GetStatsAsync(restaurantId, thirtyDaysAgo, today, ct);

        ViewBag.Restaurant = restaurant;
        return View(stats);
    }

    [HttpGet("{restaurantId:long}/plans")]
    public async Task<IActionResult> Plans(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var plans = await monetizationService.ListPlansAsync(ct);
        var currentSub = await monetizationService.GetActiveSubscriptionAsync(restaurantId, ct);

        ViewBag.Restaurant = restaurant;
        ViewBag.CurrentSubscription = currentSub;
        return View(plans);
    }

    [HttpPost("{restaurantId:long}/plans/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckoutPlan(long restaurantId, string planCode, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var order = await monetizationService.CreateOrderAsync(restaurantId, CurrentUserId(), planCode, ct);
        return View("OrderConfirmation", order);
    }

    [HttpPost("{restaurantId:long}/social")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSocial(long restaurantId, RestaurantSocialMedia media, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(media.Platform) || string.IsNullOrWhiteSpace(media.ProfileUrl))
        {
            TempData["ErrorMessage"] = "Plataforma e URL de perfil são obrigatórios.";
            return RedirectToAction(nameof(Manage), new { restaurantId });
        }

        await portalService.UpsertSocialMediaAsync(restaurantId, media, ct);
        TempData["SuccessMessage"] = "Rede social oficial registrada com sucesso!";
        return RedirectToAction(nameof(Manage), new { restaurantId });
    }
}
