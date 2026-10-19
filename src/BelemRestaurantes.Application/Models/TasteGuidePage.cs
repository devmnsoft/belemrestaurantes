using BelemRestaurantes.Domain.Data;

namespace BelemRestaurantes.Application.Models;

public sealed record TasteGuidePage(TasteEntry Entry, IReadOnlyList<RestaurantView> Guide, IReadOnlyList<RestaurantView> Directory);
