using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[AllowAnonymous,Route("cuia-rotas")]
public sealed class VisitController(IRestaurantReadRepository restaurants,IOperatingHoursRepository hours,IMobilityProvider mobility,MySqlConnectionFactory factory):Controller
{
    [HttpGet]
    [HttpGet("/qual-onibus")]
    public Task<IActionResult> Index(long restaurantId=0,string mode="transit",DateTime? localTime=null,string? q=null,CancellationToken ct=default)
        =>RenderAsync(new(restaurantId,null,mode,localTime),false,q,ct);
    [HttpPost("consultar"),ValidateAntiForgeryToken,RequestSizeLimit(8192)]
    public Task<IActionResult> Consult(VisitRequest request,CancellationToken ct)=>RenderAsync(request,true,null,ct);
    private async Task<IActionResult> RenderAsync(VisitRequest request,bool consult,string? query,CancellationToken ct)
    {
        Response.Headers["Cache-Control"]="private, no-store";Response.Headers["Referrer-Policy"]="no-referrer";
        if(request.Mode is not ("transit" or "walking" or "bicycling" or "driving" or "boat")||request.Origin?.Length>250
           ||request.MaxWalkMeters is <100 or >10000||request.Preference is not ("less_walking" or "less_transfers" or "lowest_cost")
           ||request.LocalTime is {} wall&&(wall<DateTime.UtcNow.AddDays(-1)||wall>DateTime.UtcNow.AddDays(100)))return BadRequest("Confira os dados e escolha uma data nos próximos 100 dias.");
        var restaurant=request.RestaurantId>0?await restaurants.GetPublishedAsync(request.RestaurantId,ct):null;
        if(request.RestaurantId>0&&restaurant is null)return NotFound();
        var schedule=restaurant is null?new OperatingSchedule(null,[]):await hours.GetAsync(restaurant.Id,ct);
        var local=request.LocalTime??TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,OpeningHours.ParaZone).DateTime;
        var instant=new DateTimeOffset(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),OpeningHours.ParaZone.GetUtcOffset(local));
        var assessment=OpeningHours.Evaluate(schedule,instant,DateTimeOffset.UtcNow);
        RouteResult? routes=null;
        if(consult&&restaurant is not null)routes=await mobility.PlanAsync(request,restaurant,ct);
        // A departure request does not itself establish restaurant arrival time.
        if(consult&&!request.Arrival)assessment=new("unknown","Funcionamento na chegada precisa ser conferido após consultar a duração do trajeto.");
        var choices=restaurant is null?(await restaurants.SearchAsync(new(Term:query,PageSize:30),ct)).Items:[];
        await using var db=await factory.OpenAsync(ct);
        var fares=(await db.QueryAsync<FareView>(new CommandDefinition("""
            SELECT f.service_name ServiceName,f.mode Mode,f.amount Amount,f.valid_from ValidFrom,f.valid_until ValidUntil,
                f.conditions Conditions,f.confidence Confidence,s.source_url SourceUrl,s.name SourceName
            FROM mobility_fares f JOIN mobility_sources s ON s.id=f.source_id
            WHERE s.enabled=TRUE AND s.expires_at>UTC_TIMESTAMP(6) AND f.valid_from<=@date AND f.valid_until>=@date
            ORDER BY f.service_name,f.valid_from DESC LIMIT 50
            """,new{date=local.Date},cancellationToken:ct))).AsList();
        return View("Index",new VisitPlanModel(restaurant,request,schedule,assessment,routes,choices,fares));
    }
}
