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

        async Task<T> LoadAsync<T>(Func<Task<T>> load, T fallback, string module)
        {
            try { return await load(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Homepage: módulo {Module} indisponível.", module);
                return fallback;
            }
        }
        var cuiaTask = LoadAsync(() => cuiaService.GetTopRankingsAsync("melhores_belem", 6, ct), cuiaBadges, "Selo Cuia");
        var expTask = LoadAsync(() => experienceService.ListPublicFeedAsync(1, 4, null, null, ct), experiences, "Comunidade");
        var citiesTask = Task.FromResult<IReadOnlyList<string>>(BelemRestaurantes.Domain.Data.ParaOfficialCatalog.Municipalities
            .Select(c=>c.Name).OrderBy(n=>n).ToArray());
        var catTask = LoadAsync(() => restaurantRepository.ListCategoriesAsync(ct), categories, "Categorias");
        var trailsTask = LoadAsync(() => trailService.ListTrailsAsync(ct), trails, "Roteiros");
        var featuredTask = LoadAsync(async () => (await restaurantRepository.SearchAsync(new RestaurantSearchQuery(Page: 1, PageSize: 6), ct)).Items, featured, "Restaurantes");
        await Task.WhenAll(cuiaTask, expTask, citiesTask, catTask, trailsTask, featuredTask);
        cuiaBadges = await cuiaTask;
        experiences = await expTask;
        cities = await citiesTask;
        categories = await catTask;
        trails = await trailsTask;
        featured = await featuredTask;

        var model = new HomeViewModel(cuiaBadges, experiences, cities, categories, trails, featured);
        return View(model);
    }

    public IActionResult Error() => Problem(statusCode: 500, title: "Não foi possível concluir esta solicitação");

    [HttpGet("guia")]
    public IActionResult Guide() => View();

    [HttpGet("selo-cuia")]
    public async Task<IActionResult> Cuia(CancellationToken ct)
    {
        try { return View(await cuiaService.GetTopRankingsAsync("melhores_belem", 24, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ranking Cuia indisponível.");
            ViewData["Unavailable"] = true;
            return View(Array.Empty<CuiaBadgeView>());
        }
    }
}
