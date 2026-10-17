using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController(
    IRestaurantReadRepository restaurantRepository,
    ICuiaService cuiaService,
    IExperienceService experienceService,
    IMobilityTrailService trailService,
    ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        IReadOnlyList<CuiaBadgeView> cuiaBadges = Array.Empty<CuiaBadgeView>();
        IReadOnlyList<ExperienceView> experiences = Array.Empty<ExperienceView>();
        IReadOnlyList<string> cities = Array.Empty<string>();
        IReadOnlyList<string> categories = Array.Empty<string>();
        IReadOnlyList<GastronomicTrail> trails = Array.Empty<GastronomicTrail>();
        IReadOnlyList<RestaurantView> featured = Array.Empty<RestaurantView>();

        try
        {
            var cuiaTask = cuiaService.GetTopRankingsAsync("melhores_belem", 6, ct);
            var expTask = experienceService.ListPublicFeedAsync(1, 4, null, null, ct);
            var citiesTask = restaurantRepository.ListCitiesAsync(ct);
            var catTask = restaurantRepository.ListCategoriesAsync(ct);
            var trailsTask = trailService.ListTrailsAsync(ct);
            var featuredTask = restaurantRepository.SearchAsync(new RestaurantSearchQuery(Page: 1, PageSize: 6), ct);

            await Task.WhenAll(cuiaTask, expTask, citiesTask, catTask, trailsTask, featuredTask);

            cuiaBadges = await cuiaTask;
            experiences = await expTask;
            cities = await citiesTask;
            categories = await catTask;
            trails = await trailsTask;
            featured = (await featuredTask).Items;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha não fatal ao carregar dados complementares da Homepage.");
        }

        var model = new HomeViewModel(cuiaBadges, experiences, cities, categories, trails, featured);
        return View(model);
    }

    public IActionResult Error() => Problem(statusCode: 500, title: "Não foi possível concluir esta solicitação");
}