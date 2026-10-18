using BelemRestaurantes.Application.Models;
namespace BelemRestaurantes.Application.Abstractions;
public interface IParaRecipes
{
    Task<PagedResult<ParaRecipe>> ListAsync(RecipeFilter filter,bool admin,CancellationToken ct);
    Task<ParaRecipe?> GetAsync(string slug,bool admin,CancellationToken ct);
    Task<long> SaveAsync(ParaRecipe recipe,string actor,CancellationToken ct);
    Task<IReadOnlyList<ParaRecipe>> ForRestaurantAsync(long restaurantId,CancellationToken ct);
}
