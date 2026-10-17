using System;
using System.Collections.Generic;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class PartnerRulesTests
{
    [Theory]
    [InlineData("Tacacá Completo da Dona Maria", true, "tacaca")]
    [InlineData("Maniçoba Tradicional Paraense", true, "manicoba")]
    [InlineData("Pato no Tucupi com Jambu", true, "pato_tucupi")]
    [InlineData("Açaí do Combu com Tapioca", true, "acai")]
    [InlineData("Filhote Grelhado na Brasa", true, "peixe_regional")]
    [InlineData("Torta de Cupuaçu com Castanha", true, "sobremesa_paraense")]
    [InlineData("Pizza de Calabresa", false, "outro")]
    [InlineData("Hambúrguer Artesanal", false, "outro")]
    public void RegionalDishes_AreIdentifiedAndClassifiedCorrectly(string dishName, bool expectedIsRegional, string expectedType)
    {
        var isRegional = PartnerRules.IsRecognizedRegionalDish(dishName);
        var type = PartnerRules.ClassifyRegionalType(dishName);

        Assert.Equal(expectedIsRegional, isRegional);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void IsOpenNow_ReturnsTrue_WhenWithinRegularSchedule()
    {
        // Quarta-feira, 15:00 UTC = 12:00 Belém (UTC-3)
        var utcNow = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero); // 14/10/2026 é quarta-feira (DayOfWeek = 3)

        var regularSchedules = new List<RestaurantSchedule>
        {
            new() { DayOfWeek = 3, IsClosed = false, OpenTime = new TimeSpan(11, 30, 0), CloseTime = new TimeSpan(23, 0, 0) }
        };

        var isOpen = PartnerRules.IsOpenNow(regularSchedules, [], utcNow, out var statusText);

        Assert.True(isOpen);
        Assert.Contains("Aberto agora", statusText);
    }

    [Fact]
    public void IsOpenNow_RespectsSpecialHolidayException()
    {
        // 14/10/2026 às 12:00 Belém, mas com feriado especial marcado como FECHADO
        var utcNow = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero);

        var regularSchedules = new List<RestaurantSchedule>
        {
            new() { DayOfWeek = 3, IsClosed = false, OpenTime = new TimeSpan(11, 30, 0), CloseTime = new TimeSpan(23, 0, 0) }
        };

        var specialSchedules = new List<RestaurantSpecialSchedule>
        {
            new() { SpecificDate = new DateOnly(2026, 10, 14), IsClosed = true, Reason = "Círio de Nazaré" }
        };

        var isOpen = PartnerRules.IsOpenNow(regularSchedules, specialSchedules, utcNow, out var statusText);

        Assert.False(isOpen);
        Assert.Contains("Fechado hoje (Círio de Nazaré)", statusText);
    }
}
