using System.Globalization;

namespace BelemRestaurantes.Application.Models;

public sealed record OpeningWindow(int StartMinute,int EndMinute);
public sealed record OpeningDay(int Day,bool Closed,bool AllDay,IReadOnlyList<OpeningWindow> Windows,DateOnly? Date=null);
public sealed class OperationInfo
{
    public int Version { get; set; }
    public string Authority { get; set; }="";
    public string SourceUrl { get; set; }="";
    public DateTime ConfirmedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? TemporaryClosedUntil { get; set; }
    public bool ReservationOnly { get; set; }
    public string? AccessibilityNote { get; set; }
}
public sealed record OpeningAssessment(string State,string Message,DateTimeOffset? NextChange=null);
public sealed record OperatingSchedule(OperationInfo? Info,IReadOnlyList<OpeningDay> Days);
public sealed record VisitRequest(long RestaurantId,string? Origin,string Mode="transit",DateTime? LocalTime=null,
    bool Arrival=true,int MaxWalkMeters=1000,string Preference="less_walking",bool Accessible=false,string Occasion="any",int VisitMinutes=60);
public sealed record VisitWindow(DateTimeOffset Start,DateTimeOffset End,string Note);
public sealed record RouteStep(string Mode,string Instruction,string? Line=null,string? Board=null,string? Alight=null,
    string? Departure=null,string? Arrival=null,int? DistanceMeters=null);
public sealed record RouteAlternative(string Description,int DistanceMeters,int DurationSeconds,
    IReadOnlyList<RouteStep> Steps,string? Fare=null,string? EncodedPolyline=null);
public sealed record RouteResult(string Provider,string State,string Message,DateTimeOffset QueriedAt,
    IReadOnlyList<RouteAlternative> Alternatives,string ExternalUrl);

public static class VisitLinks
{
    public static string Destination(RestaurantView restaurant)=>restaurant.Latitude is {} lat&&restaurant.Longitude is {} lng
        &&double.IsFinite(lat)&&double.IsFinite(lng)&&lat is >=-90 and <=90&&lng is >=-180 and <=180
        ?FormattableString.Invariant($"{lat},{lng}"):$"{restaurant.Name}, {restaurant.Address}, {restaurant.City}, Pará, Brasil";
    public static string Maps(RestaurantView restaurant,string? origin,string mode)
    {
        var mapped=mode is "walking" or "bicycling" or "driving"?mode:"transit";
        return "https://www.google.com/maps/dir/?api=1&destination="+Uri.EscapeDataString(Destination(restaurant))+"&travelmode="+mapped
            +(string.IsNullOrWhiteSpace(origin)?"":"&origin="+Uri.EscapeDataString(origin.Trim()));
    }
    public static string SharePath(long id,DateTime? when,string mode)=>$"/cuia-rotas?restaurantId={id}&mode={Uri.EscapeDataString(mode)}"
        +(when is null?"":"&localTime="+Uri.EscapeDataString(when.Value.ToString("yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture)));
}
