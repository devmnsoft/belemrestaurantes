using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Abstractions;

public interface IOperatingHoursRepository
{
    Task<OperatingSchedule> GetAsync(long restaurantId,CancellationToken ct);
    Task SaveAsync(long restaurantId,int expectedVersion,OperationInfo info,IReadOnlyList<OpeningDay> days,string actor,CancellationToken ct);
}
public interface IMobilityProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<RouteResult> PlanAsync(VisitRequest request,RestaurantView restaurant,CancellationToken ct);
}
