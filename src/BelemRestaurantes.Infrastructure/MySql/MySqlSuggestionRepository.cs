using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
namespace BelemRestaurantes.Infrastructure.MySql;
public sealed class MySqlSuggestionRepository(MySqlConnectionFactory factory) : ISuggestionRepository
{
    public async Task<long> AddAsync(string name, string city, string? website, string? notes, System.Threading.CancellationToken ct)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new Dapper.CommandDefinition(
            "INSERT INTO suggestions (name, city, website, notes, status, submitted_at) VALUES (@name, @city, @website, @notes, 'pending', UTC_TIMESTAMP(6))",
            new { name, city, website, notes },
            cancellationToken: ct));
        var id = await connection.ExecuteScalarAsync<long>(new Dapper.CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: ct));
        return id;
    }
}