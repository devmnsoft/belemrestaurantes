using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        using var scope = _factory.Services.CreateScope();
        var recipes = scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IParaRecipes>();
        const string slug = "tacaca-em-casa";
        var created = await recipes.GetAsync(slug, false, default) is null;
        if (created)
        {
            await recipes.SaveAsync(new BelemRestaurantes.Application.Models.ParaRecipe
            {
                Slug = slug,
                Title = "Tacacá em casa",
                Subtitle = "Adaptação editorial para teste do guia de substituição.",
                Ingredients = ["tucupi", "jambu", "goma de tapioca"],
                Steps = ["Ferver o tucupi.", "Hidratar a goma.", "Servir na cuia."],
                Status = "published"
            }, "fixture-admin", default);
        }

        try
        {
            var response = await client.GetAsync("/api/cuia-ia/dica-receita/" + slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("Tucupi", json);
        }
        finally
        {
            if (created)
            {
                await using var db = await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default);
                var id = await db.ExecuteScalarAsync<long?>("SELECT id FROM para_recipes WHERE slug=@slug", new { slug });
                if (id is not null)
                {
                    await db.ExecuteAsync("DELETE FROM para_recipe_versions WHERE recipe_id=@id", new { id });
                    await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='recipe' AND entity_id=@id AND actor_subject='fixture-admin'", new { id });
                    await db.ExecuteAsync("DELETE FROM para_recipes WHERE id=@id", new { id });
                }
            }
        }
    }
}
