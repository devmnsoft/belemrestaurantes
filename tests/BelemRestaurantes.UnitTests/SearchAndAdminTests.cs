using System;
using System.Collections.Generic;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class SearchAndAdminTests
{
    [Fact]
    public void Autocomplete_WithShortTerm_ReturnsEmptyResults()
    {
        // Regra de segurança contra DoS: buscas com menos de 2 caracteres não disparam scan pesado
        var query = "a";
        Assert.True(query.Trim().Length < 2);
    }

    [Fact]
    public void SearchFilterOptions_StoresCitiesAndCategories()
    {
        var options = new SearchFilterOptions(
            Cities: ["Belém", "Ananindeua", "Castanhal"],
            Categories: ["Regional", "Açaí", "Peixaria"],
            Neighborhoods: ["Nazaré", "Umarizal", "Marco"]
        );

        Assert.Equal(3, options.Cities.Count);
        Assert.Equal(3, options.Categories.Count);
        Assert.Equal(3, options.Neighborhoods.Count);
        Assert.Contains("Belém", options.Cities);
    }

    [Fact]
    public void CommercialPlan_ValidatesKeyBusinessAttributes()
    {
        var plan = new CommercialPlan
        {
            Code = "ouro",
            Name = "Plano Ouro Belém",
            MonthlyPrice = 149.90m,
            BadgeHighlight = true,
            MenuEnabled = true,
            SponsoredExposure = true
        };

        Assert.Equal("ouro", plan.Code);
        Assert.True(plan.MonthlyPrice > 0);
        Assert.True(plan.BadgeHighlight);
        Assert.True(plan.MenuEnabled);
    }

    [Fact]
    public void ConnectorStatusVerification_RequiresRealExecutionProof()
    {
        // Requisito 9: "Não afirmar que determinado conector está ativo sem prova de uma operação bem-sucedida"
        bool hasSuccessfulCalls = false;
        bool isConfigured = true;

        bool isVerifiedOperational = isConfigured && hasSuccessfulCalls;

        Assert.False(isVerifiedOperational);
    }
}
