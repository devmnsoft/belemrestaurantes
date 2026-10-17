using System;
using System.Collections.Generic;
using System.Linq;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Domain.Rules;

public static class PartnerRules
{
    private static readonly HashSet<string> RegionalDishes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tacaca", "tacacá", "manicoba", "maniçoba", "pato no tucupi", "pato ao tucupi",
        "acai", "açaí", "vatapa", "vatapá", "caruru", "filhote", "filhote na brasa",
        "filhote ao tucupi", "pescada amarela", "pirarucu", "tambaqui", "caldeirada",
        "camarao regional", "camarão regional", "torta de cupuacu", "torta de cupuaçu",
        "pudim de tapioca", "mousse de bacuri", "sorvete de acai", "sorvete de tapioca",
        "bolinho de pirarucu", "unha de caranguejo", "casquinha de caranguejo"
    };

    public static bool IsRecognizedRegionalDish(string? dishName)
    {
        if (string.IsNullOrWhiteSpace(dishName)) return false;
        var lower = dishName.Trim().ToLowerInvariant();
        return RegionalDishes.Any(r => lower.Contains(r, StringComparison.OrdinalIgnoreCase));
    }

    public static string ClassifyRegionalType(string? dishName)
    {
        if (string.IsNullOrWhiteSpace(dishName)) return "outro";
        var lower = dishName.Trim().ToLowerInvariant();

        if (lower.Contains("tacac")) return "tacaca";
        if (lower.Contains("maniçob") || lower.Contains("manicob")) return "manicoba";
        if (lower.Contains("pato")) return "pato_tucupi";
        if (lower.Contains("açaí") || lower.Contains("acai")) return "acai";
        if (lower.Contains("vatap")) return "vatapa";
        if (lower.Contains("caruru")) return "caruru";
        if (lower.Contains("filhote") || lower.Contains("pescada") || lower.Contains("pirarucu") || lower.Contains("tambaqui") || lower.Contains("peixe")) return "peixe_regional";
        if (lower.Contains("caranguejo")) return "caranguejo";
        if (lower.Contains("cupuaçu") || lower.Contains("cupuacu") || lower.Contains("bacuri") || lower.Contains("tapioca")) return "sobremesa_paraense";

        return "outro";
    }

    public static bool IsOpenNow(
        IEnumerable<RestaurantSchedule> regularSchedules,
        IEnumerable<RestaurantSpecialSchedule> specialSchedules,
        DateTimeOffset nowUtc,
        out string statusText)
    {
        // Fuso de Belém / Pará: UTC-3 (America/Belem)
        var belemTime = nowUtc.ToOffset(TimeSpan.FromHours(-3));
        var todayDate = DateOnly.FromDateTime(belemTime.DateTime);
        var currentTime = belemTime.TimeOfDay;
        var dayOfWeek = (byte)belemTime.DayOfWeek;

        // 1. Checar exceção para data específica (feriados / horários especiais)
        var special = specialSchedules.FirstOrDefault(s => s.SpecificDate == todayDate);
        if (special != null)
        {
            if (special.IsClosed)
            {
                statusText = $"Fechado hoje ({special.Reason})";
                return false;
            }

            if (special.OpenTime.HasValue && special.CloseTime.HasValue)
            {
                if (currentTime >= special.OpenTime.Value && currentTime <= special.CloseTime.Value)
                {
                    statusText = $"Aberto hoje até {special.CloseTime.Value:hh\\:mm} ({special.Reason})";
                    return true;
                }

                if (currentTime < special.OpenTime.Value)
                {
                    statusText = $"Abre hoje às {special.OpenTime.Value:hh\\:mm} ({special.Reason})";
                    return false;
                }

                statusText = $"Fechou às {special.CloseTime.Value:hh\\:mm} ({special.Reason})";
                return false;
            }
        }

        // 2. Checar horário regular da semana
        var regular = regularSchedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
        if (regular == null || regular.IsClosed || !regular.OpenTime.HasValue || !regular.CloseTime.HasValue)
        {
            statusText = "Fechado hoje";
            return false;
        }

        if (currentTime >= regular.OpenTime.Value && currentTime <= regular.CloseTime.Value)
        {
            statusText = $"Aberto agora • Fecha às {regular.CloseTime.Value:hh\\:mm}";
            return true;
        }

        if (currentTime < regular.OpenTime.Value)
        {
            statusText = $"Fechado • Abre hoje às {regular.OpenTime.Value:hh\\:mm}";
            return false;
        }

        statusText = $"Fechado • Fechou às {regular.CloseTime.Value:hh\\:mm}";
        return false;
    }
}
