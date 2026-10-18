using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="RestaurantModerator"),Route("admin/mobilidade")]
public sealed class AdminMobilityController(IOperatingHoursRepository hours,MySqlConnectionFactory factory):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long restaurantId=0,CancellationToken ct=default)
    {
        var model=new OperatingHoursInput{RestaurantId=restaurantId};
        if(restaurantId>0)
        {
            var schedule=await hours.GetAsync(restaurantId,ct);
            if(schedule.Info is {} i){model.Version=i.Version;model.SourceUrl=i.SourceUrl;model.ConfirmedAt=i.ConfirmedAt;model.ExpiresAt=i.ExpiresAt;model.TemporaryClosedUntil=i.TemporaryClosedUntil;model.ReservationOnly=i.ReservationOnly;model.AccessibilityNote=i.AccessibilityNote;}
            string Windows(OpeningDay day)=>string.Join(';',day.Windows.Select(w=>$"{w.StartMinute/60:00}:{w.StartMinute%60:00}-{w.EndMinute/60:00}:{w.EndMinute%60:00}"));
            foreach(var d in schedule.Days.Where(d=>d.Date is null)){model.Days[d.Day].State=d.Closed?"closed":d.AllDay?"24h":"open";model.Days[d.Day].Windows=Windows(d);}
            model.Exceptions=string.Join('\n',schedule.Days.Where(d=>d.Date is not null).Select(d=>$"{d.Date:yyyy-MM-dd}="+(d.Closed?"closed":d.AllDay?"24h":Windows(d))));
        }
        await LoadAsync(ct);return View(model);
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        ViewData["Sources"]=(await db.QueryAsync(new CommandDefinition("SELECT * FROM mobility_sources ORDER BY id DESC LIMIT 100",cancellationToken:ct))).AsList();
        ViewData["Usage"]=(await db.QueryAsync(new CommandDefinition("SELECT * FROM mobility_api_usage ORDER BY usage_day DESC LIMIT 14",cancellationToken:ct))).AsList();
        ViewData["Fares"]=(await db.QueryAsync(new CommandDefinition("SELECT f.*,s.name source_name,s.source_url FROM mobility_fares f JOIN mobility_sources s ON s.id=f.source_id ORDER BY f.id DESC LIMIT 100",cancellationToken:ct))).AsList();
    }
    [HttpPost("horarios"),ValidateAntiForgeryToken,RequestSizeLimit(32768)]
    public async Task<IActionResult> Save(OperatingHoursInput model,CancellationToken ct)
    {
        try
        {
            if(!ModelState.IsValid||model.Days.Count!=7||model.Days.Select(d=>d.Day).Distinct().Count()!=7||model.Days.Any(d=>d.Day is <0 or >6||d.State is not ("unknown" or "open" or "closed" or "24h")))throw new ArgumentException("Confira os sete dias da semana.");
            var days=model.Days.Where(d=>d.State!="unknown").Select(d=>new OpeningDay(d.Day,d.State=="closed",d.State=="24h",d.State=="open"?OpeningHours.ParseWindows(d.Windows):[])).ToList();
            foreach(var line in (model.Exceptions??"").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
            {
                var parts=line.Split('=',2,StringSplitOptions.TrimEntries);
                if(parts.Length!=2||!DateOnly.TryParseExact(parts[0],"yyyy-MM-dd",out var date))throw new ArgumentException("Use uma exceção por linha: AAAA-MM-DD=closed, 24h ou turnos.");
                days.Add(new((int)date.DayOfWeek,parts[1]=="closed",parts[1]=="24h",parts[1] is "closed" or "24h"?[]:OpeningHours.ParseWindows(parts[1]),date));
            }
            await hours.SaveAsync(model.RestaurantId,model.Version,new(){Authority="moderator",SourceUrl=model.SourceUrl,ConfirmedAt=DateTime.SpecifyKind(model.ConfirmedAt,DateTimeKind.Utc),ExpiresAt=DateTime.SpecifyKind(model.ExpiresAt,DateTimeKind.Utc),TemporaryClosedUntil=model.TemporaryClosedUntil,ReservationOnly=model.ReservationOnly,AccessibilityNote=model.AccessibilityNote},days,User.Identity!.Name!,ct);
            TempData["Flash"]="Horários gravados com fonte, validade e histórico.";return RedirectToAction(nameof(Index),new{model.RestaurantId});
        }
        catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);await LoadAsync(ct);return View("Index",model);}
    }
    [Authorize(Policy="PlatformOperator"),HttpPost("fonte"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Source(string name,string kind,string sourceUrl,string terms,DateTime expiresAt,bool enabled,CancellationToken ct)
    {
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(name)||name.Length>160||kind is not ("official_reference" or "river" or "gtfs" or "intercity")||!Https(sourceUrl)||string.IsNullOrWhiteSpace(terms)||terms.Length>500||expiresAt<=DateTime.UtcNow||expiresAt>DateTime.UtcNow.AddDays(366))return BadRequest();
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO mobility_sources(name,kind,source_url,terms,enabled,checked_at,expires_at)
            VALUES(@name,@kind,@sourceUrl,@terms,@enabled,UTC_TIMESTAMP(6),@expiresAt)
            ON DUPLICATE KEY UPDATE name=@name,kind=@kind,terms=@terms,enabled=@enabled,checked_at=UTC_TIMESTAMP(6),expires_at=@expiresAt
            """,new{name,kind,sourceUrl,terms,enabled,expiresAt},tx,cancellationToken:ct));
        await AuditAsync(db,tx,"mobility.source_update",new{name,kind,sourceUrl,terms,enabled,expiresAt},ct);await tx.CommitAsync(ct);
        TempData["Flash"]="Fonte cadastrada com condições de uso. Referências não são feeds integrados.";return RedirectToAction(nameof(Index));
    }
    [Authorize(Policy="PlatformOperator"),HttpPost("tarifa"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Fare(long sourceId,string serviceName,string mode,string amount,DateTime validFrom,DateTime validUntil,string conditions,string confidence,CancellationToken ct)
    {
        if(!ModelState.IsValid||!System.Text.RegularExpressions.Regex.IsMatch(amount??"","^\\d{1,5}([,.]\\d{1,2})?$")
            ||!decimal.TryParse(amount!.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value)
            ||string.IsNullOrWhiteSpace(serviceName)||serviceName.Length>160||mode is not ("bus" or "brt" or "boat" or "intercity")||value is <0 or >99999||validUntil<validFrom||string.IsNullOrWhiteSpace(conditions)||conditions.Length>500||confidence is not ("official" or "estimated" or "partner"))return BadRequest();
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM mobility_sources WHERE id=@sourceId AND enabled=TRUE AND expires_at>UTC_TIMESTAMP(6)",new{sourceId},tx,cancellationToken:ct))!=1)return BadRequest("Fonte inativa ou vencida.");
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO mobility_fares(source_id,service_name,mode,amount,valid_from,valid_until,conditions,confidence,created_at) VALUES(@sourceId,@serviceName,@mode,@amount,@validFrom,@validUntil,@conditions,@confidence,UTC_TIMESTAMP(6))",new{sourceId,serviceName,mode,amount=value,validFrom,validUntil,conditions,confidence},tx,cancellationToken:ct));
        await AuditAsync(db,tx,"mobility.fare_create",new{sourceId,serviceName,mode,amount,validFrom,validUntil,conditions,confidence},ct);await tx.CommitAsync(ct);
        TempData["Flash"]="Tarifa registrada com vigência e auditoria.";return RedirectToAction(nameof(Index));
    }
    private static bool Https(string? url)=>url?.Length<=350&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo);
    private Task AuditAsync(MySqlConnector.MySqlConnection db,MySqlConnector.MySqlTransaction tx,string action,object data,CancellationToken ct)=>db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,@action,'mobility',@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,action,metadata=JsonSerializer.Serialize(data)},tx,cancellationToken:ct));
}
