namespace BelemRestaurantes.Application.Abstractions;
public interface ISuggestionRepository { System.Threading.Tasks.Task<long> AddAsync(string name, string city, string? website, string? notes, System.Threading.CancellationToken ct, long? targetRestaurantId = null); }
