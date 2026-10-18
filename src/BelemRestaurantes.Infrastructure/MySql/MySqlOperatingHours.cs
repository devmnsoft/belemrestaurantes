using System.Text.Json;
using BelemRestaurantes.Application.Models;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlOperatingHours(MySqlConnectionFactory factory):IOperatingHoursRepository
{
    public async Task<OperatingSchedule> GetAsync(long restaurantId,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var info=await db.QuerySingleOrDefaultAsync<OperationInfo>(new CommandDefinition("""
            SELECT version Version,authority Authority,source_url SourceUrl,confirmed_at ConfirmedAt,expires_at ExpiresAt,
                temporary_closed_until TemporaryClosedUntil,reservation_only ReservationOnly,accessibility_note AccessibilityNote
            FROM restaurant_operation_info WHERE restaurant_id=@restaurantId
            """,new{restaurantId},cancellationToken:ct));
        var days=new List<OpeningDay>();
        var regular=await db.QueryAsync(new CommandDefinition("SELECT * FROM restaurant_schedules WHERE restaurant_id=@restaurantId",new{restaurantId},cancellationToken:ct));
        foreach(var r in regular)days.Add(Map(r,null));
        var special=await db.QueryAsync(new CommandDefinition("SELECT * FROM restaurant_special_schedules WHERE restaurant_id=@restaurantId AND specific_date>=DATE_SUB(UTC_DATE(),INTERVAL 1 DAY)",new{restaurantId},cancellationToken:ct));
        foreach(var r in special)days.Add(Map(r,DateOnly.FromDateTime((DateTime)r.specific_date)));
        return new(info,days);
    }
    private static OpeningDay Map(dynamic r,DateOnly? date)
    {
        var windows=new List<OpeningWindow>();
        if(r.open_time is TimeSpan open&&r.close_time is TimeSpan close)windows.Add(new((int)open.TotalMinutes,(int)close.TotalMinutes));
        if(r.additional_intervals is string json)windows.AddRange(JsonSerializer.Deserialize<OpeningWindow[]>(json)??[]);
        return new(date is null?(int)r.day_of_week:(int)date.Value.DayOfWeek,(bool)r.is_closed,(bool)r.is_24_hours,windows,date);
    }
    public async Task SaveAsync(long restaurantId,int expectedVersion,OperationInfo info,IReadOnlyList<OpeningDay> days,string actor,CancellationToken ct)
    {
        if(info.Authority is not ("moderator" or "owner" or "external")||!Uri.TryCreate(info.SourceUrl,UriKind.Absolute,out var source)||source.Scheme!="https"||info.SourceUrl.Length>350
            ||info.ExpiresAt<=DateTime.UtcNow||info.ExpiresAt>DateTime.UtcNow.AddDays(366)||info.ConfirmedAt>DateTime.UtcNow.AddMinutes(5)||info.ConfirmedAt< DateTime.UtcNow.AddYears(-1)
            ||info.ExpiresAt<=info.ConfirmedAt||info.AccessibilityNote?.Length>500)throw new ArgumentException("Fonte HTTPS, confirmação e validade de até um ano são obrigatórias.");
        if(days.Count>380||days.Select(d=>(d.Day,d.Date)).Distinct().Count()!=days.Count||days.Any(d=>d.Day is <0 or >6||d.Windows.Count>4||d.Windows.Any(w=>w.StartMinute is <0 or >1439||w.EndMinute is <0 or >1439||w.StartMinute==w.EndMinute)||(!d.Closed&&!d.AllDay&&d.Windows.Count==0)))throw new ArgumentException("Confira os dias e turnos informados.");
        foreach(var d in days.Where(d=>!d.Closed&&!d.AllDay))
            BelemRestaurantes.Application.Services.OpeningHours.ParseWindows(string.Join(';',d.Windows.Select(w=>$"{w.StartMinute/60:00}:{w.StartMinute%60:00}-{w.EndMinute/60:00}:{w.EndMinute%60:00}")));
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        var restaurant=await db.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT id FROM restaurants WHERE id=@restaurantId FOR UPDATE",new{restaurantId},tx,cancellationToken:ct));
        if(restaurant is null)throw new ArgumentException("Restaurante não encontrado.");
        var current=await db.QuerySingleOrDefaultAsync(new CommandDefinition("SELECT * FROM restaurant_operation_info WHERE restaurant_id=@restaurantId FOR UPDATE",new{restaurantId},tx,cancellationToken:ct));
        if((current is null?0:(int)current.version)!=expectedVersion)throw new ArgumentException("Outro usuário atualizou os horários. Recarregue antes de salvar.");
        if(current is not null&&(string)current.authority=="owner"&&info.Authority!="owner")throw new ArgumentException("Horários confirmados pelo parceiro têm prioridade. Solicite a revisão ao responsável.");
        var before=await db.QueryAsync(new CommandDefinition("SELECT * FROM restaurant_schedules WHERE restaurant_id=@restaurantId",new{restaurantId},tx,cancellationToken:ct));
        var beforeSpecial=await db.QueryAsync(new CommandDefinition("SELECT * FROM restaurant_special_schedules WHERE restaurant_id=@restaurantId",new{restaurantId},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO restaurant_operation_history(restaurant_id,actor,snapshot_json,created_at) VALUES(@restaurantId,@actor,@snapshot,UTC_TIMESTAMP(6))",new{restaurantId,actor,snapshot=JsonSerializer.Serialize(new{previous=current,regular=before,special=beforeSpecial,next=info,days})},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM restaurant_schedules WHERE restaurant_id=@restaurantId",new{restaurantId},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM restaurant_special_schedules WHERE restaurant_id=@restaurantId",new{restaurantId},tx,cancellationToken:ct));
        foreach(var d in days)
        {
            var first=d.Windows.FirstOrDefault();
            var args=new{restaurantId,day=d.Day,closed=d.Closed,allDay=d.AllDay,open=first is null?(TimeSpan?)null:TimeSpan.FromMinutes(first.StartMinute),close=first is null?(TimeSpan?)null:TimeSpan.FromMinutes(first.EndMinute),additional=JsonSerializer.Serialize(d.Windows.Skip(1)),date=d.Date?.ToDateTime(TimeOnly.MinValue)};
            var sql=d.Date is null?"INSERT INTO restaurant_schedules(restaurant_id,day_of_week,is_closed,open_time,close_time,additional_intervals,is_24_hours) VALUES(@restaurantId,@day,@closed,@open,@close,@additional,@allDay)":"INSERT INTO restaurant_special_schedules(restaurant_id,specific_date,is_closed,open_time,close_time,additional_intervals,is_24_hours,reason) VALUES(@restaurantId,@date,@closed,@open,@close,@additional,@allDay,'Horário especial confirmado')";
            await db.ExecuteAsync(new CommandDefinition(sql,args,tx,cancellationToken:ct));
        }
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO restaurant_operation_info(restaurant_id,version,authority,source_url,confirmed_at,expires_at,temporary_closed_until,reservation_only,accessibility_note,updated_by)
            VALUES(@restaurantId,1,@Authority,@SourceUrl,@ConfirmedAt,@ExpiresAt,@TemporaryClosedUntil,@ReservationOnly,@AccessibilityNote,@actor)
            ON DUPLICATE KEY UPDATE version=version+1,authority=@Authority,source_url=@SourceUrl,confirmed_at=@ConfirmedAt,expires_at=@ExpiresAt,
              temporary_closed_until=@TemporaryClosedUntil,reservation_only=@ReservationOnly,accessibility_note=@AccessibilityNote,updated_by=@actor
            """,new{restaurantId,info.Authority,info.SourceUrl,info.ConfirmedAt,info.ExpiresAt,info.TemporaryClosedUntil,info.ReservationOnly,info.AccessibilityNote,actor},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'restaurant.hours_update','restaurant',@restaurantId,@metadata,UTC_TIMESTAMP(6))",new{actor,restaurantId,metadata=JsonSerializer.Serialize(new{expectedVersion,info.Authority,info.SourceUrl})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }
}
