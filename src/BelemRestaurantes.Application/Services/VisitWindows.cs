using BelemRestaurantes.Application.Models;
namespace BelemRestaurantes.Application.Services;
public static class VisitWindows
{
    /// <summary>Janelas de atendimento, sem inferir lotação, avaliação, reserva ou disponibilidade de transporte.</summary>
    public static IReadOnlyList<VisitWindow> Suggest(OperatingSchedule schedule,DateTimeOffset after,DateTimeOffset now,string occasion="any",int minutes=60)
    {
        if(minutes is <15 or >180||occasion is not("any" or "lunch" or "dinner" or "coffee" or "quick"))return [];
        var info=schedule.Info;if(info is null)return [];
        var start=after>now?after:now;
        start=new DateTimeOffset((start.UtcTicks+TimeSpan.TicksPerMinute*15-1)/(TimeSpan.TicksPerMinute*15)*(TimeSpan.TicksPerMinute*15),TimeSpan.Zero);
        var results=new List<VisitWindow>();
        var limit=start.AddDays(7);
        while(start<limit&&results.Count<3)
        {
            var local=TimeZoneInfo.ConvertTime(start,OpeningHours.ParaZone);
            var hour=local.Hour+local.Minute/60d;
            var fits=occasion switch{"lunch"=>hour>=11&&hour+minutes/60d<=15,"dinner"=>hour>=18&&hour+minutes/60d<=23,"coffee"=>hour>=7&&hour+minutes/60d<=11,"quick"=>hour>=8&&hour+minutes/60d<=21,_=>hour>=8&&hour+minutes/60d<=23};
            var end=start.AddMinutes(minutes);
            var status=OpeningHours.Evaluate(schedule,start,now);
            if(fits&&status.State=="open"&&status.NextChange>=end&&OpeningHours.Evaluate(schedule,end.AddTicks(-1),now).State=="open")
            {
                results.Add(new(start,end,info.ReservationOnly?"Janela no horário informado; somente com reserva confirmada pelo estabelecimento.":"Janela no horário informado. Confirme o atendimento; não há estimativa de fila ou lotação."));
                start=start.AddHours(4);continue;
            }
            start=start.AddMinutes(15);
        }
        return results;
    }
}
