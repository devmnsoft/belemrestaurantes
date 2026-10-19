using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class CuiaAiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CuiaAiEndpointTests(WebApplicationFactory<Program> factory)
    {
        var cs = DatabaseEnvironment.ConnectionString()!;
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) =>
            {
                c.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["ConnectionStrings:MySql"] = cs,
                    ["Discovery:Enabled"] = "false"
                });
            });
        });
    }

    [Fact]
    public async Task CuiaAi_Page_Returns200_WithConciergeInterface()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/cuia-ia");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Cuia IA", html);
        Assert.Contains("Concierge da <em>Amazônia</em>", html);
        Assert.Contains("ai-prompt", html);
        Assert.Contains("cuia-ai.js", html);
    }

    [Fact]
    public async Task CuiaAi_WithInitialQuery_RendersInitialResponse()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/cuia-ia?q=Onde+tomar+tacaca");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Cuia IA", html);
        Assert.Contains("Recomendações da Cuia IA", html);
    }

    [Fact]
    public async Task CuiaAi_RecipeSubstitutions_ReturnsTips()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/cuia-ia/dica-receita/tacaca-em-casa");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Tucupi", json);
    }
}
