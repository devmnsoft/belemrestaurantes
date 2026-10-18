using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Services;

public static class OpeningHours
{
    public static TimeZoneInfo ParaZone { get; }=TimeZoneInfo.FindSystemTimeZoneById("America/Belem");
    public static IReadOnlyList<OpeningWindow> ParseWindows(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return [];
        var windows=new List<OpeningWindow>();
        foreach(var part in text.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            var sides=part.Split('-',StringSplitOptions.TrimEntries);
            if(sides.Length!=2||!TimeOnly.TryParseExact(sides[0],"HH:mm",out var start)||!TimeOnly.TryParseExact(sides[1],"HH:mm",out var end)||start==end)
                throw new ArgumentException("Use HH:mm-HH:mm; separe turnos por ponto e vírgula. Para 24 horas, marque a opção própria.");
            windows.Add(new(start.Hour*60+start.Minute,end.Hour*60+end.Minute));
        }
        if(windows.Count>4)throw new ArgumentException("Máximo de quatro turnos por dia.");
        var minutes=new HashSet<int>();
        foreach(var w in windows)
            for(var i=w.StartMinute;i<(w.EndMinute>w.StartMinute?w.EndMinute:w.EndMinute+1440);i++)
                if(!minutes.Add(i%1440))throw new ArgumentException("Os turnos não podem se sobrepor.");
        return windows;
    }
    public static OpeningAssessment Evaluate(OperatingSchedule schedule,DateTimeOffset instant,DateTimeOffset now)
    {
        var local=TimeZoneInfo.ConvertTime(instant,ParaZone);
        var info=schedule.Info;
        if(info is null || DateTime.SpecifyKind(info.ExpiresAt,DateTimeKind.Utc)<=now.UtcDateTime || DateTime.SpecifyKind(info.ConfirmedAt,DateTimeKind.Utc)>now.UtcDateTime.AddMinutes(5))
            return new("unknown","Horário não confirmado");
        if(info.TemporaryClosedUntil is {} closed && local.Date<=closed.Date)
            return new("temporary","Fechamento provisório informado até "+closed.ToString("dd/MM/yyyy"));
        var date=DateOnly.FromDateTime(local.Date);
        OpeningDay? For(DateOnly d)=>schedule.Days.FirstOrDefault(x=>x.Date==d)??schedule.Days.FirstOrDefault(x=>x.Date is null&&x.Day==(int)d.DayOfWeek);
        var today=For(date);
        if(today?.Date is not null&&today.Closed)return new("closed","Fechado nesta data · horário especial");
        DateTimeOffset At(DateOnly d,int minute)
        {var wall=d.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);return new(wall,ParaZone.GetUtcOffset(wall));}
        var windows=new List<(DateTimeOffset Start,DateTimeOffset End)>();
        for(int offset=-1;offset<=8;offset++)
        {
            var d=date.AddDays(offset);var day=For(d);
            if(day is null||day.Closed)continue;
            if(day.AllDay) windows.Add((At(d,0),At(d,1440)));
            else foreach(var w in day.Windows) windows.Add((At(d,w.StartMinute),At(d,w.EndMinute>w.StartMinute?w.EndMinute:w.EndMinute+1440)));
        }
        var active=windows.Where(w=>w.Start<=instant&&w.End>instant).OrderByDescending(w=>w.End).FirstOrDefault();
        if(active!=default)return new("open",(today?.AllDay==true?"Aberto conforme horário · atendimento 24 horas":$"Aberto conforme horário · fecha às {TimeZoneInfo.ConvertTime(active.End,ParaZone):HH:mm}")+(info.ReservationOnly?" · Somente com reserva":""),active.End);
        if(today is null)return new("unknown","Horário não confirmado para este dia");
        var next=windows.Where(w=>w.Start>instant).OrderBy(w=>w.Start).FirstOrDefault();
        return next==default?new("closed","Fechado no horário escolhido · próxima abertura não informada"):
            new("closed",$"Fechado no horário escolhido · abre {TimeZoneInfo.ConvertTime(next.Start,ParaZone):dd/MM às HH:mm}",next.Start);
    }
}
