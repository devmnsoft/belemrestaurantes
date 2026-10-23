using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Sprint 65 — cache negativo de logradouro no banco: escopo por (logradouro, cidade),
/// validade em dias e atualização apenas por confirmação real.</summary>
[Collection("MySql")]
public sealed class Sprint65StreetCacheTests
{
    private const string Sk = "rua_cache_sprint65";
    private const string CkBelem = "belem";
    private const string CkSantarem = "santarem";

    [Fact]
    public async Task NegativeCacheIsScopedByCityAndExpirable()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        using (var scope = root.CreateScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IGeocodingQueue>();

            await using (var db = await dbFactory.OpenAsync(default))
                await db.ExecuteAsync("DELETE FROM geocoding_street_cache WHERE street_key=@sk", new { sk = Sk });

            Assert.False(await queue.IsStreetRecentlyEmptyAsync(Sk, CkBelem, 7, default));
            Assert.False(await queue.IsStreetRecentlyEmptyAsync(Sk, CkSantarem, 7, default));

            await queue.RecordStreetEmptyAsync(Sk, CkBelem, default);
            Assert.True(await queue.IsStreetRecentlyEmptyAsync(Sk, CkBelem, 7, default));
            // Mesma rua em outra cidade continua sem cobertura.
            Assert.False(await queue.IsStreetRecentlyEmptyAsync(Sk, CkSantarem, 7, default));
            // Janela menor (1 dia) ainda cobre a entrada fresca.
            Assert.True(await queue.IsStreetRecentlyEmptyAsync(Sk, CkBelem, 1, default));

            // Expiração: entrada mais velha que a janela deixa de valer...
            await using (var db = await dbFactory.OpenAsync(default))
                await db.ExecuteAsync("""
                    UPDATE geocoding_street_cache SET learned_at=DATE_SUB(NOW(6), INTERVAL 8 DAY)
                    WHERE street_key=@sk AND city_key=@c
                    """, new { sk = Sk, c = CkBelem });
            Assert.False(await queue.IsStreetRecentlyEmptyAsync(Sk, CkBelem, 7, default));
            // ...e a próxima confirmação real a renova.
            await queue.RecordStreetEmptyAsync(Sk, CkBelem, default);
            Assert.True(await queue.IsStreetRecentlyEmptyAsync(Sk, CkBelem, 7, default));
        }
        await using (var db = await dbFactory.OpenAsync(default))
            await db.ExecuteAsync("DELETE FROM geocoding_street_cache WHERE street_key=@sk", new { sk = Sk });
    }

    [Fact]
    public async Task ClaimReleasedForLackOfQuotaDoesNotRatchetAttempts()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var queue = sp.GetRequiredService<IGeocodingQueue>();
        await using var db = await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var token = Guid.NewGuid().ToString("N")[..8];
        var id = await db.ExecuteScalarAsync<long>("""
            INSERT INTO restaurants(name,normalized_name,city,normalized_city,address,category,status,created_at,updated_at)
            VALUES(@name,LOWER(@name),'Belém','belem',@address,'Teste','published',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID()
            """, new { name = "Fixture CacheS65 " + token, address = "Travessa Cache S65 " + token + ", São Brás" });
        try
        {
            // Linha forçadamente a primeira da fila (next_run anterior a qualquer existente).
            await db.ExecuteAsync("""
                INSERT INTO geocoding_tasks(restaurant_id,state,next_run_at,updated_at)
                VALUES(@id,'pending','1999-01-01 00:00:00',UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE state='pending',attempts=0,next_run_at='1999-01-01 00:00:00',lease_until=NULL
                """, new { id });

            // Três ciclos de "pego sem orçamento": o contador não pode ratchetar rumo a 'failed'.
            for (var round = 0; round < 3; round++)
            {
                var task = await queue.ClaimAsync(default);
                Assert.NotNull(task);
                Assert.Equal(id, task.RestaurantId);
                await queue.ReleaseClaimAsync(id, Math.Max(0, task.Attempts - 1), default);
                Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT attempts FROM geocoding_tasks WHERE restaurant_id=@id", new { id }));
                await db.ExecuteAsync("UPDATE geocoding_tasks SET next_run_at='1999-01-01 00:00:00' WHERE restaurant_id=@id", new { id });
            }
            Assert.Equal("pending", await db.ExecuteScalarAsync<string>("SELECT state FROM geocoding_tasks WHERE restaurant_id=@id", new { id }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM geocoding_tasks WHERE restaurant_id=@id", new { id });
            await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id", new { id });
        }
    }
}
