using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                    ["Discovery:Enabled"] = "false",
                    // Hermeticidade (Sprint 75): sem isso, os user-secrets locais alimentam o Gemini real e o desfecho do Ask
                    // depende da cota gratuita do dia — com pool zerado a resposta é determinística, com pool limpo o modelo
                    // responde à vontade. No CI (sem secrets) já era sempre determinístico; aqui igualamos ao CI.
                    ["Ai:Gemini:ApiKey"] = null,
                    ["Ai:Groq:ApiKey"] = null,
                    ["Ai:DeepSeek:ApiKey"] = null,
                    ["Ai:Drael:ApiKey"] = null
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
        Assert.Contains("Cuia IA — Seu guia de sabores do Pará", html);
        Assert.Contains("O que você pode perguntar", html);
        Assert.Contains("Perguntar à Cuia", html);
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
        Assert.Contains("Sua resposta", html);
        Assert.Contains("cuia-ai-kind", html);
        Assert.Contains("Resposta feita com", html);
    }

    [Fact]
    public async Task CuiaAi_ScriptInQuery_IsEncoded()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/cuia-ia?q=" + Uri.EscapeDataString("<script>alert(1)</script>"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
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

    [Fact]
    public async Task Ask_WithValidToken_ReturnsJson()
    {
        using var client = Client();
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "Jantar romantico na Estação das Docas", city = "Belém" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!DOCTYPE", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("answerMarkdown", out var answer));
        Assert.False(string.IsNullOrWhiteSpace(answer.GetString()));
        Assert.True(json.RootElement.TryGetProperty("recommendations", out var recommendations));
        if (recommendations.GetArrayLength() == 0)
        {
            Assert.DoesNotContain("Nazaré", body);
            Assert.DoesNotContain("alta gastronomia", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Não encontramos restaurantes conferidos", body);
        }
    }

    [Fact]
    public async Task Ask_WithoutToken_ReturnsProblemJson()
    {
        using var client = Client();
        await TokenAsync(client);
        var response = await client.PostAsJsonAsync("/api/cuia-ia/ask", new { prompt = "tacacá" });
        await AssertProblem(response, HttpStatusCode.BadRequest, "antiforgery");
    }

    [Fact]
    public async Task Ask_EmptyPrompt_Returns400()
    {
        using var client = Client();
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "   " });
        await AssertProblem(response, HttpStatusCode.BadRequest, "prompt_required");
    }

    [Fact]
    public async Task Ask_TooLongPrompt_Returns400()
    {
        using var client = Client();
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = new string('a', 501) });
        await AssertProblem(response, HttpStatusCode.BadRequest, "prompt_too_long");
    }

    [Fact]
    public async Task Ask_UnknownApiPath_DoesNotReturnHtml()
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/cuia-ai/ask", new { prompt = "tacacá" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("<!DOCTYPE", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("unauthorized", json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Ask_RateLimit_ReturnsProblemJson()
    {
        using var factory = FactoryWith(new StubCuia());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        HttpResponseMessage? limited = null;
        for (var i = 0; i < 25 && limited?.StatusCode != HttpStatusCode.TooManyRequests; i++)
            limited = await PostAsk(client, token, new { prompt = "peixe" });
        Assert.NotNull(limited);
        await AssertProblem(limited!, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task Ask_ProviderFailure_Returns503WithoutSecrets()
    {
        using var factory = FactoryWith(new StubCuia { Failure = new HttpRequestException("Gemini key sk-secret rejected") });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "maniçoba" });
        var body = await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "provider_unavailable");
        Assert.DoesNotContain("sk-secret", body);
        Assert.DoesNotContain("Gemini", body);
    }

    [Fact]
    public async Task Ask_DatabaseFailure_Returns503()
    {
        using var factory = FactoryWith(new StubCuia { Failure = new CatalogDownException() });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "maniçoba" });
        var body = await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "catalog_unavailable");
        Assert.DoesNotContain("catalog-offline-secret", body);
    }

    [Fact]
    public async Task Ask_Timeout_Returns504()
    {
        using var factory = FactoryWith(new StubCuia { Failure = new TimeoutException("upstream timeout") });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "maniçoba" });
        var body = await AssertProblem(response, HttpStatusCode.GatewayTimeout, "timeout");
        Assert.Contains("demorou mais", body);
        Assert.DoesNotContain("upstream", body);
    }

    [Fact]
    public async Task Ask_UnexpectedException_HidesInternals()
    {
        using var factory = FactoryWith(new StubCuia { Failure = new InvalidOperationException("SELECT secret FROM users") });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "maniçoba" });
        var body = await AssertProblem(response, HttpStatusCode.InternalServerError, "unexpected");
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public async Task Ask_CanReturnNoRestaurants()
    {
        using var factory = FactoryWith(new StubCuia { Empty = true });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = await TokenAsync(client);
        var response = await PostAsk(client, token, new { prompt = "lugar inexistente" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(0, json.RootElement.GetProperty("recommendations").GetArrayLength());
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private WebApplicationFactory<Program> FactoryWith(StubCuia stub)
    {
        var cs = DatabaseEnvironment.ConnectionString()!;
        return _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = cs,
                ["Discovery:Enabled"] = "false"
            }));
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICuiaAiService>();
                services.AddSingleton<ICuiaAiService>(stub);
            });
        });
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/cuia-ia");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return System.Net.WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostAsk(HttpClient client, string token, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cuia-ia/ask")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        request.Headers.Accept.ParseAdd("application/json");
        return client.SendAsync(request);
    }

    private static async Task<string> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, response.StatusCode);
        Assert.DoesNotContain("<!DOCTYPE", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("correlationId").GetString()));
        Assert.Equal(code is "prompt_required" or "prompt_too_long" ? JsonValueKind.False : JsonValueKind.True, json.RootElement.GetProperty("retryable").ValueKind);
        return body;
    }

    private sealed class CatalogDownException() : System.Data.Common.DbException("catalog-offline-secret");

    private sealed class StubCuia : ICuiaAiService
    {
        public Exception? Failure { get; init; }
        public bool Empty { get; init; }

        public Task<CuiaAiResponse> AskAsync(CuiaAiQuery query, CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            return Task.FromResult(new CuiaAiResponse(
                "Resposta de teste, sem provedor externo.",
                Empty ? [] : [new CuiaAiRecommendation(1, "Casa de teste", "Belém", "Campina", "Restaurante", "Rua de teste", null, "Categoria conferida no catálogo.", null, null, null)],
                [],
                [],
                [],
                "teste"));
        }

        public Task<string> GetRestaurantAdviceAsync(long restaurantId, CancellationToken ct) => Task.FromResult("ok");
        public Task<string> GetRecipeSubstitutionsAsync(string recipeSlug, CancellationToken ct) => Task.FromResult("ok");
        public CuiaAiStatus GetStatus() => new([], true, "teste");
    }
}
