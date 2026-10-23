using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using System.Security.Cryptography;
using System.Text;
namespace BelemRestaurantes.Infrastructure.MySql;
public sealed class MySqlSuggestionRepository(MySqlConnectionFactory factory) : ISuggestionRepository
{
    public async Task<long> AddAsync(string name, string city, string? website, string? notes, System.Threading.CancellationToken ct, long? targetRestaurantId = null)
    {
        using var connection = factory.Create();
        await connection.OpenAsync(ct);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{NameNormalizer.Normalize(name)}|{NameNormalizer.Normalize(city)}|{targetRestaurantId}|{(targetRestaurantId is null ? "" : notes)}")));
        // Named locks serialize the same pending suggestion across application instances.
        // Rejected/approved submissions do not prevent a later indication or correction.
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT GET_LOCK(@key,3)", new { key }, cancellationToken: ct)) != 1)
            throw new TimeoutException("Não foi possível reservar esta indicação para revisão.");
        try
        {
            var existing = await connection.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT id FROM suggestions WHERE status='pending' AND (dedupe_key=@key OR (dedupe_key IS NULL AND name=@name AND city=@city AND target_restaurant_id <=> @targetRestaurantId)) LIMIT 1",
                new { name, city, targetRestaurantId, key }, cancellationToken: ct));
            if (existing is {} id) return id;
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO suggestions (name, city, website, notes, target_restaurant_id, status, submitted_at, dedupe_key) VALUES (@name, @city, @website, @notes, @targetRestaurantId, 'pending', UTC_TIMESTAMP(6), @key)",
                new { name, city, website, notes, targetRestaurantId, key }, cancellationToken: ct));
            return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: ct));
        }
        finally { await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT RELEASE_LOCK(@key)", new { key }, commandTimeout: 3)); }
    }
}
