using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
namespace BelemRestaurantes.Infrastructure.MySql;
public sealed class MySqlSuggestionRepository(MySqlConnectionFactory factory) : ISuggestionRepository
{
    public async Task<long> AddAsync(string name, string city, string? website, string? notes, System.Threading.CancellationToken ct, long? targetRestaurantId = null)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new Dapper.CommandDefinition(
            "INSERT INTO suggestions (name, city, website, notes, target_restaurant_id, status, submitted_at) VALUES (@name, @city, @website, @notes, @targetRestaurantId, 'pending', UTC_TIMESTAMP(6))",
            new { name, city, website, notes, targetRestaurantId },
            cancellationToken: ct));
        var id = await connection.ExecuteScalarAsync<long>(new Dapper.CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: ct));
        return id;
    }
}
