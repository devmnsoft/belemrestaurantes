using BelemRestaurantes.Domain.Entities;
namespace BelemRestaurantes.Web.Models;
public sealed record CrossingsViewModel(IReadOnlyList<FareView> Fares,IReadOnlyList<GastronomicTrail> Trails);
