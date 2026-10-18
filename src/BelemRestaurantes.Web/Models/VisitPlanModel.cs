using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Web.Models;
public sealed record VisitPlanModel(RestaurantView? Restaurant,VisitRequest Request,OperatingSchedule Schedule,
    OpeningAssessment Assessment,RouteResult? Routes,IReadOnlyList<RestaurantView> Choices,IReadOnlyList<FareView> Fares);
public sealed class FareView
{
    public string ServiceName { get; set; }="";
    public string Mode { get; set; }="";
    public decimal Amount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public string Conditions { get; set; }="";
    public string Confidence { get; set; }="";
    public string SourceUrl { get; set; }="";
    public string SourceName { get; set; }="";
}
