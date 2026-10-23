using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Sprint 66 — cache de consulta Nominatim no banco: escopo pela consulta exata, validade pelo
/// dia UTC em que a resposta real ocorreu e contador de hits.</summary>
[Collection("MySql")]
public sealed class Sprint66QueryCacheTests
{
    [Fact]
    public async Task QueryCacheIsScopedByExactQueryAndExpiresWithUtcDay()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var queue = sp.GetRequiredService<IGeocodingQueue>();
        await using var db = await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var t0 = await db.ExecuteScalarAsync<DateTime>("SELECT UTC_TIMESTAMP(6)");
        var token = Guid.NewGuid().ToString("N")[..8];
        var address = "Travessa Cache S66 " + token + ", São Brás";
        const string city = "Belém";
        try
        {
            Assert.Null(await queue.TryGetCachedPoolAsync(address, city, default));

            var pool = new List<NominatimCandidate>
            {
                new(-1.4667324, -48.4918527, "house", 4242, "Travessa Cache S66", token, city, "Pará"),
                new(-1.4670000, -48.4920000, "road", 4243, null, null, null, null)
            };
            await queue.StoreCachedPoolAsync(address, city, pool, default);
            Assert.Equal(pool, await queue.TryGetCachedPoolAsync(address, city, default));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>(
                "SELECT COALESCE(SUM(hits),0) FROM geocoding_query_cache WHERE created_at >= @t0", new { t0 }));

            // Mesma cidade com número diferente, e mesmo endereço em outra cidade: sem acerto cruzado.
            Assert.Null(await queue.TryGetCachedPoolAsync("Travessa Cache S66 " + token + ", 9999, São Brás", city, default));
            Assert.Null(await queue.TryGetCachedPoolAsync(address, "Santarém", default));

            // A entrada envelhece um dia UTC: para de valer...
            await db.ExecuteAsync("UPDATE geocoding_query_cache SET day=DATE_SUB(day, INTERVAL 1 DAY) WHERE created_at >= @t0", new { t0 });
            Assert.Null(await queue.TryGetCachedPoolAsync(address, city, default));
            // ...e uma nova busca real do dia atual recria somente a linha de hoje para a mesma chave.
            await queue.StoreCachedPoolAsync(address, city, pool, default);
            Assert.NotNull(await queue.TryGetCachedPoolAsync(address, city, default));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM geocoding_query_cache WHERE created_at >= @t0 AND day=UTC_DATE()", new { t0 }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM geocoding_query_cache WHERE created_at >= @t0", new { t0 });
        }
    }
}
