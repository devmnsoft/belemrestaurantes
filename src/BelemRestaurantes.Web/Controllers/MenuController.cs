using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

public sealed class MenuController(
    IMenuService menuService,
    IPartnerAuthorizationService authService,
    IRestaurantReadRepository restaurantRepository,
    IPartnerPortalService portalService) : Controller
{
    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> AuthorizePartnerAsync(long restaurantId, CancellationToken ct)
    {
        if (!User.Identity?.IsAuthenticated ?? true) return false;
        return await authService.IsOwnerOrManagerAsync(restaurantId, CurrentUserId(), ct);
    }

    // --- VISÃO PÚBLICA DO CARDÁPIO DIGITAL ---

    [AllowAnonymous]
    [HttpGet("cardapio/{restaurantId:long}")]
    public async Task<IActionResult> PublicMenu(long restaurantId, CancellationToken ct)
    {
        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        // Registra estatística de visualização de cardápio
        await portalService.RecordStatClickAsync(restaurantId, "menu", ct);

        var menu = await menuService.GetRestaurantMenuAsync(restaurantId, ct);
        var profile = await portalService.GetRestaurantProfileAsync(restaurantId, ct);

        ViewBag.Restaurant = restaurant;
        ViewBag.Profile = profile;
        return View(menu);
    }

    // --- GESTÃO DO CARDÁPIO PELO COMERCIANTE PARCEIRO ---

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpGet("partner/{restaurantId:long}/menu")]
    public async Task<IActionResult> ManageMenu(long restaurantId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var restaurant = await restaurantRepository.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return NotFound();

        var menu = await menuService.GetRestaurantMenuAsync(restaurantId, ct);
        ViewBag.Restaurant = restaurant;

        return View(menu);
    }

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpPost("partner/{restaurantId:long}/menu/category")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory(long restaurantId, string name, int displayOrder, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Nome da categoria é obrigatório.";
            return RedirectToAction(nameof(ManageMenu), new { restaurantId });
        }

        await menuService.AddCategoryAsync(restaurantId, name.Trim(), displayOrder, ct);
        TempData["SuccessMessage"] = "Categoria adicionada com sucesso!";
        return RedirectToAction(nameof(ManageMenu), new { restaurantId });
    }

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpPost("partner/{restaurantId:long}/menu/category/{categoryId:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(long restaurantId, long categoryId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        await menuService.DeleteCategoryAsync(restaurantId, categoryId, ct);
        TempData["SuccessMessage"] = "Categoria removida com sucesso.";
        return RedirectToAction(nameof(ManageMenu), new { restaurantId });
    }

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpPost("partner/{restaurantId:long}/menu/item")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(long restaurantId, MenuItem item, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(item.Name) || item.CategoryId <= 0)
        {
            TempData["ErrorMessage"] = "Nome do item e categoria são obrigatórios.";
            return RedirectToAction(nameof(ManageMenu), new { restaurantId });
        }

        item.RestaurantId = restaurantId;
        await menuService.AddMenuItemAsync(item, ct);
        TempData["SuccessMessage"] = "Prato/Produto adicionado ao cardápio com sucesso!";
        return RedirectToAction(nameof(ManageMenu), new { restaurantId });
    }

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpPost("partner/{restaurantId:long}/menu/item/{itemId:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(long restaurantId, long itemId, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        await menuService.DeleteMenuItemAsync(restaurantId, itemId, ct);
        TempData["SuccessMessage"] = "Item removido do cardápio com sucesso.";
        return RedirectToAction(nameof(ManageMenu), new { restaurantId });
    }

    [Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
    [HttpPost("partner/{restaurantId:long}/menu/suggest-ai")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuggestAi(long restaurantId, string dishName, string? ingredients, CancellationToken ct)
    {
        if (!await AuthorizePartnerAsync(restaurantId, ct)) return Forbid();

        var suggestion = await menuService.SuggestDishDescriptionWithAiAsync(dishName, ingredients, ct);
        return Json(new { success = true, suggestion });
    }
}
