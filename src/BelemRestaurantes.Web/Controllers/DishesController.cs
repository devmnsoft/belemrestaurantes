using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous]
public sealed class DishesController(ITasteGuide taste) : Controller
{
    [HttpGet("/pratos")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Pratos do Pará";
        ViewData["MetaDescription"] = "Tacacá, maniçoba, pato no tucupi, açaí e peixe. A camada Guia recomenda; a ficha só mostra cadastro.";
        ViewData["AtlasEditorial"] = true;
        return View(ParaTasteCatalog.Dishes);
    }

    [HttpGet("/pratos/{slug}")]
    public Task<IActionResult> Dish(string slug, CancellationToken ct) => PageAsync(slug, "prato", ct);

    [HttpGet("/ocasioes")]
    public IActionResult Occasions()
    {
        ViewData["Title"] = "Ocasiões à mesa";
        ViewData["MetaDescription"] = "Domingo, Círio, 48 horas, almoço executivo, família e ilha. Sem horário de barco inventado.";
        ViewData["AtlasEditorial"] = true;
        return View("Occasions", ParaTasteCatalog.Occasions);
    }

    [HttpGet("/ocasioes/{slug}")]
    public Task<IActionResult> Occasion(string slug, CancellationToken ct) => PageAsync(slug, "ocasiao", ct);

    private async Task<IActionResult> PageAsync(string slug, string kind, CancellationToken ct)
    {
        var page = await taste.GetAsync(slug, ct);
        if (page is null || page.Entry.Kind != kind) return NotFound();
        ViewData["Title"] = page.Entry.Title;
        ViewData["MetaDescription"] = page.Entry.Summary;
        ViewData["AtlasEditorial"] = true;
        return View("Details", page);
    }
}
