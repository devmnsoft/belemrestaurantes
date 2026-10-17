using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record AutocompleteResult(
    string Type, // "restaurant", "dish", "category", "city"
    long Id,
    string Title,
    string Subtitle,
    string Url);

public sealed record SearchFilterOptions(
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Neighborhoods);

public interface ISearchService
{
    Task<IReadOnlyList<AutocompleteResult>> AutocompleteAsync(string query, CancellationToken ct);
    Task<SearchFilterOptions> GetFilterOptionsAsync(CancellationToken ct);
}
