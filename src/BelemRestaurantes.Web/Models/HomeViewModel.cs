using System.Collections.Generic;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Web.Models;

public sealed record HomeViewModel(
    IReadOnlyList<CuiaBadgeView> CuiaHighlights,
    IReadOnlyList<ExperienceView> RecentExperiences,
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> Categories,
    IReadOnlyList<GastronomicTrail> Trails,
    IReadOnlyList<RestaurantView> FeaturedRestaurants);
