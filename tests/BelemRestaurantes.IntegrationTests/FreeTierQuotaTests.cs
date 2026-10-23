using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class FreeTierQuotaTests
{
    [Fact]
    public async Task ConcurrentTokenReservationsAreAtomicAndRejectOversizedRequests()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();
        var quota=services.GetRequiredService<IFreeTierQuota>();
        var service="cuia_session_"+Guid.NewGuid().ToString("N");
        try {
            var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>quota.TryReserveUnitsAsync(service,100,100,30,default)));
            Assert.Equal(3,results.Count(x=>x));
            Assert.False(await quota.TryReserveUnitsAsync(service,100,100,101,default));
            Assert.True(await quota.TryReserveUnitsAsync(service,100,100,10,default));
            Assert.False(await quota.TryReserveUnitsAsync(service,100,100,1,default));
            await using var read=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            var periods=(await read.QueryAsync<string>("SELECT period FROM external_free_usage WHERE service=@service ORDER BY period",new{service})).ToArray();
            Assert.Equal(new[]{"00000000","00000001"},periods);
        }
        finally {
            await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            await db.ExecuteAsync("DELETE FROM external_free_usage WHERE service=@service",new{service});
        }
    }

    [Fact]
    public async Task FreeTierRespectsPauseWithoutEnablingPaidSpending()
    {
        var services = ServiceContainer.Build();
        await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var provider = "fixture_" + Guid.NewGuid().ToString("N")[..16];
        await using var db = await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            await db.ExecuteAsync("INSERT INTO api_budgets(provider,monthly_limit_micros_usd,monthly_limit_requests,paused) VALUES(@provider,0,0,TRUE)", new { provider });
            var ledger = services.GetRequiredService<ICostLedger>();
            Assert.False(await ledger.CanUseFreeTierAsync(provider, default));
            await db.ExecuteAsync("UPDATE api_budgets SET paused=FALSE WHERE provider=@provider", new { provider });
            Assert.True(await ledger.CanUseFreeTierAsync(provider, default));
            Assert.False(await ledger.HasBudgetRemaining(provider, 0m, default));
            Assert.False(await ledger.CanUseFreeTierAsync("missing_fixture", default));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM api_budgets WHERE provider=@provider", new { provider });
            (services as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentReservationsRespectBothLimitsAndPersistAcrossScopes()
    {
        var services = ServiceContainer.Build();
        await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var service = "fixture_" + Guid.NewGuid().ToString("N");
        var factory = services.GetRequiredService<MySqlConnectionFactory>();
        try
        {
            var tasks = Enumerable.Range(0, 12).Select(async _ =>
            {
                using var scope = services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IFreeTierQuota>().TryReserveAsync(service, 3, 5, default);
            });
            Assert.Equal(3, (await Task.WhenAll(tasks)).Count(ok => ok));
            using var next = services.CreateScope();
            var quota = next.ServiceProvider.GetRequiredService<IFreeTierQuota>();
            Assert.False(await quota.TryReserveAsync(service, 3, 5, default));
            Assert.True(await quota.TryReserveAsync(service, 10, 5, default));
            Assert.True(await quota.TryReserveAsync(service, 10, 5, default));
            Assert.False(await quota.TryReserveAsync(service, 10, 5, default));
            await using var db = await factory.OpenAsync(default);
            var counts = (await db.QueryAsync<int>("SELECT requests FROM external_free_usage WHERE service=@service", new { service })).ToArray();
            Assert.Equal(2, counts.Length); Assert.All(counts, count => Assert.Equal(5, count));
        }
        finally
        {
            await using var db = await factory.OpenAsync(default);
            await db.ExecuteAsync("DELETE FROM external_free_usage WHERE service=@service", new { service });
            (services as IDisposable)?.Dispose();
        }
    }
}
