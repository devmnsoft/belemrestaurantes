using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public sealed class CuiaAiTests
{
    private sealed class FakeRestaurantRepo : IRestaurantReadRepository
    {
        public Task<PagedResult<RestaurantView>> SearchAsync(RestaurantSearchQuery query, CancellationToken ct)
        {
            var items = new List<RestaurantView>
            {
                new RestaurantView(1, "Tacacá da Dona Maria", "Belém", "Nazaré", "Tacacá", "Av. Nazaré, 100", null, null, null, null, null, null, DateTimeOffset.UtcNow),
                new RestaurantView(2, "Peixaria do Combu", "Belém", "Ilha do Combu", "Peixaria", "Furo do Combu", null, null, null, null, null, null, DateTimeOffset.UtcNow)
            };
            return Task.FromResult(new PagedResult<RestaurantView>(items, 1, 24, items.Count));
        }

        public Task<RestaurantView?> GetPublishedAsync(long id, CancellationToken ct)
        {
            if (id == 1)
            {
                return Task.FromResult<RestaurantView?>(new RestaurantView(1, "Tacacá da Dona Maria", "Belém", "Nazaré", "Tacacá", "Av. Nazaré, 100", null, null, null, null, null, null, DateTimeOffset.UtcNow));
            }
            return Task.FromResult<RestaurantView?>(null);
        }

        public Task<IReadOnlyList<string>> ListCitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["Belém", "Ananindeua", "Bragança"]);

        public Task<IReadOnlyList<string>> ListNeighborhoodsAsync(string city, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["Nazaré", "Umarizal"]);

        public Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["Tacacá", "Peixaria", "Açaí"]);
    }

    private sealed class FakeRecipesRepo : IParaRecipes
    {
        public Task<PagedResult<ParaRecipe>> ListAsync(RecipeFilter filter, bool admin, CancellationToken ct)
        {
            var list = new List<ParaRecipe>
            {
                new ParaRecipe
                {
                    Id = 1,
                    Slug = "tacaca-em-casa",
                    Title = "Tacacá em Casa",
                    Subtitle = "Como preparar o clássico",
                    Category = "Prato típico",
                    City = "Belém",
                    Difficulty = "Fácil",
                    PrepMinutes = 15,
                    CookMinutes = 30,
                    Servings = 4,
                    Ingredients = ["tucupi", "jambu", "goma", "camarão"],
                    Steps = ["Ferva o tucupi", "Monte a cuia"]
                }
            };
            return Task.FromResult(new PagedResult<ParaRecipe>(list, 1, 10, list.Count));
        }

        public Task<ParaRecipe?> GetAsync(string slug, bool admin, CancellationToken ct)
        {
            if (slug == "tacaca-em-casa")
            {
                return Task.FromResult<ParaRecipe?>(new ParaRecipe
                {
                    Id = 1,
                    Slug = "tacaca-em-casa",
                    Title = "Tacacá em Casa",
                    Subtitle = "Como preparar o clássico",
                    Category = "Prato típico",
                    City = "Belém",
                    Difficulty = "Fácil",
                    PrepMinutes = 15,
                    CookMinutes = 30,
                    Servings = 4,
                    Ingredients = ["tucupi", "jambu", "goma", "camarão"],
                    Steps = ["Ferva o tucupi", "Monte a cuia"]
                });
            }
            return Task.FromResult<ParaRecipe?>(null);
        }

        public Task<long> SaveAsync(ParaRecipe recipe, string actor, CancellationToken ct) => Task.FromResult(1L);
        public Task<IReadOnlyList<ParaRecipe>> ForRestaurantAsync(long restaurantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ParaRecipe>>([]);
    }

    [Fact]
    public async Task AskAsync_ReturnsCoherentAmazonianRecommendations_WithoutCrashing()
    {
        var service = new CuiaAiService(
            new FakeRestaurantRepo(),
            new FakeRecipesRepo(),
            [],
            NullLogger<CuiaAiService>.Instance);

        var response = await service.AskAsync(new CuiaAiQuery("Onde tomar tacacá em Belém?"), default);

        Assert.NotNull(response);
        Assert.NotEmpty(response.AnswerMarkdown);
        Assert.Contains("tacacá", response.AnswerMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(response.Recommendations);
        Assert.NotEmpty(response.RegionalCuriosities);
        Assert.NotEmpty(response.SuggestedFollowUps);
        Assert.Contains("Cuia Knowledge Engine", response.EngineUsed);
    }

    [Fact]
    public async Task AskAsync_HandlesRiverAndCombuQueries()
    {
        var service = new CuiaAiService(
            new FakeRestaurantRepo(),
            new FakeRecipesRepo(),
            [],
            NullLogger<CuiaAiService>.Instance);

        var response = await service.AskAsync(new CuiaAiQuery("Almoço ribeirinho na Ilha do Combu com barco"), default);

        Assert.NotNull(response);
        Assert.Contains("Combu", response.AnswerMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("popopô", response.AnswerMarkdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRestaurantAdviceAsync_ProvidesSommelierTips()
    {
        var service = new CuiaAiService(
            new FakeRestaurantRepo(),
            new FakeRecipesRepo(),
            [],
            NullLogger<CuiaAiService>.Instance);

        var advice = await service.GetRestaurantAdviceAsync(1, default);

        Assert.NotNull(advice);
        Assert.Contains("Tacacá da Dona Maria", advice);
        Assert.Contains("jambu", advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRecipeSubstitutionsAsync_ProvidesAdaptationGuide()
    {
        var service = new CuiaAiService(
            new FakeRestaurantRepo(),
            new FakeRecipesRepo(),
            [],
            NullLogger<CuiaAiService>.Instance);

        var tips = await service.GetRecipeSubstitutionsAsync("tacaca-em-casa", default);

        Assert.NotNull(tips);
        Assert.Contains("Tucupi", tips);
        Assert.Contains("Jambu", tips);
    }
}
