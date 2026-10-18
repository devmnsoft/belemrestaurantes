using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Mobility;

namespace BelemRestaurantes.UnitTests;
public sealed class VisitPlanningTests
{
    private static readonly DateTimeOffset Now=new(2026,10,19,15,0,0,TimeSpan.Zero); // Monday noon in Pará
    private static OperatingSchedule Schedule(params OpeningDay[] days)=>new(new(){Authority="owner",ConfirmedAt=Now.UtcDateTime.AddDays(-1),ExpiresAt=Now.UtcDateTime.AddDays(90)},days);
    [Fact] public void UnknownAndExpiredAreNeverReportedOpen()
    {
        Assert.Equal("unknown",OpeningHours.Evaluate(new(null,[]),Now,Now).State);
        var schedule=Schedule(new OpeningDay(1,false,true,[]));schedule.Info!.ExpiresAt=Now.UtcDateTime;
        Assert.Equal("unknown",OpeningHours.Evaluate(schedule,Now,Now).State);
    }
    [Fact] public void MultipleTurnsAndExclusiveClosingBoundary()
    {
        var schedule=Schedule(new OpeningDay(1,false,false,OpeningHours.ParseWindows("11:00-14:00;18:00-23:00")));
        Assert.Equal("open",OpeningHours.Evaluate(schedule,Now,Now).State);
        var closed=OpeningHours.Evaluate(schedule,Now.AddHours(2),Now);
        Assert.Equal("closed",closed.State);Assert.Equal(21,closed.NextChange!.Value.UtcDateTime.Hour);
    }
    [Fact] public void OvernightAndDatedClosureTakePrecedence()
    {
        var schedule=Schedule(new(0,false,false,OpeningHours.ParseWindows("20:00-02:00")),new(1,true,false,[]));
        Assert.Equal("open",OpeningHours.Evaluate(schedule,Now.AddHours(-11),Now).State);
        schedule=schedule with{Days=[..schedule.Days,new(1,true,false,[],new DateOnly(2026,10,19))]};
        Assert.Equal("closed",OpeningHours.Evaluate(schedule,Now.AddHours(-11),Now).State);
    }
    [Fact] public void TemporaryClosureAndReservationAreExplicit()
    {
        var schedule=Schedule(new OpeningDay(1,false,true,[]));schedule.Info!.TemporaryClosedUntil=new(2026,10,19);
        Assert.Equal("temporary",OpeningHours.Evaluate(schedule,Now,Now).State);
        schedule.Info.TemporaryClosedUntil=null;schedule.Info.ReservationOnly=true;
        Assert.Contains("reserva",OpeningHours.Evaluate(schedule,Now,Now).Message);
    }
    [Theory][InlineData("09:00-12:00;11:00-14:00")][InlineData("20:00-02:00;01:00-03:00")][InlineData("12:00-12:00")][InlineData("25:00-27:00")]
    public void InvalidOrOverlappingTurnsAreRejected(string input)=>Assert.Throws<ArgumentException>(()=>OpeningHours.ParseWindows(input));
    [Fact] public void ShareNeverContainsOriginAndMapsEncodesUntrustedAddress()
    {
        var restaurant=new RestaurantView{Id=42,Name="A & B",City="Belém",Address="Rua A"};
        var link=VisitLinks.Maps(restaurant,"Casa &destination=other","transit");
        Assert.Contains("Casa%20%26destination%3Dother",link);
        Assert.DoesNotContain("origin",VisitLinks.SharePath(42,Now.DateTime,"transit"));
    }
    [Fact] public void TransitResponseIncludesOnlyProviderDataAndAppliesWalkLimit()
    {
        using var json=JsonDocument.Parse("""{"routes":[{"duration":"1200s","distanceMeters":4000,"legs":[{"steps":[{"travelMode":"WALK","distanceMeters":300,"navigationInstruction":{"instructions":"Caminhe até o ponto"}},{"travelMode":"TRANSIT","transitDetails":{"headsign":"Terminal","transitLine":{"nameShort":"999"},"stopDetails":{"departureStop":{"name":"Ponto A"},"arrivalStop":{"name":"Ponto B"}}}}]}]}]}""");
        var routes=GoogleRoutesProvider.Parse(json.RootElement,1000);
        Assert.Single(routes);Assert.Equal("999",routes[0].Steps[1].Line);Assert.Equal("Ponto A",routes[0].Steps[1].Board);Assert.Null(routes[0].Fare);
        Assert.Empty(GoogleRoutesProvider.Parse(json.RootElement,250));
    }
}
