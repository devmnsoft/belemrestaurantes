namespace BelemRestaurantes.Web.Models;
public sealed class OperatingHoursInput
{
    public long RestaurantId { get; set; }
    public int Version { get; set; }
    public string SourceUrl { get; set; }="";
    public DateTime ConfirmedAt { get; set; }=DateTime.UtcNow.Date;
    public DateTime ExpiresAt { get; set; }=DateTime.UtcNow.Date.AddDays(90);
    public DateTime? TemporaryClosedUntil { get; set; }
    public bool ReservationOnly { get; set; }
    public string? AccessibilityNote { get; set; }
    public List<OperatingDayInput> Days { get; set; }=Enumerable.Range(0,7).Select(day=>new OperatingDayInput{Day=day}).ToList();
    public string? Exceptions { get; set; }
}
public sealed class OperatingDayInput
{
    public int Day { get; set; }
    public string State { get; set; }="unknown";
    public string? Windows { get; set; }
}
