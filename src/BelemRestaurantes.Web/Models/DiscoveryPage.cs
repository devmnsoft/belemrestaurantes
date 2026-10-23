using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Web.Models;

public sealed record DiscoveryPage(string? City, string Moment, string? Dish,
    IReadOnlyList<RestaurantView> Places, IReadOnlyList<ParaRecipe> Recipes);
