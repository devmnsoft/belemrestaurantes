using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint48StabilityTests
{
    [Fact]
    public async Task CancelledBlockedBatchLeavesNoDecisionAndCanBeRecoveredIdempotently()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var bulk=services.GetRequiredService<MySqlBulkModeration>();var marker="s48-cancel-"+Guid.NewGuid().ToString("N");
        var place=new BelemRestaurantes.Application.Models.DiscoveredPlace(marker,marker,null,"Belém","Restaurante","Rua de teste",null,null,"https://example.org/"+marker,null,null,null,[]);
        var id=await services.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(place,"test_fixture",DateTimeOffset.UtcNow,default);
        var batch=await bulk.PrepareAsync("fixture-s48","approve_only",null,[id],false,new(),default);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try {
            await using(var blocker=await db.BeginTransactionAsync())
            {
                await db.QuerySingleAsync<long>("SELECT id FROM restaurants WHERE id=@id FOR UPDATE",new{id},blocker);
                using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>bulk.ExecuteAsync(batch.Id,"fixture-s48",cancellation.Token));
                await blocker.RollbackAsync();
            }
            Assert.Equal("pending",await db.ExecuteScalarAsync<string>("SELECT moderation_stage FROM restaurants WHERE id=@id",new{id}));
            Assert.Null(await bulk.GetResultAsync(batch.Id,"fixture-s48",default));
            Assert.NotNull(await bulk.ExecuteAsync(batch.Id,"fixture-s48",default));
            Assert.NotNull(await bulk.GetResultAsync(batch.Id,"fixture-s48",default));
            Assert.Null(await bulk.GetResultAsync(batch.Id,"other-actor",default));
            await bulk.ExecuteAsync(batch.Id,"fixture-s48",default);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE entity_type='restaurant' AND entity_id=@id AND action='restaurant.bulk_approve_only'",new{id}));
            Assert.Empty((await services.GetRequiredService<IModerationRepository>().ListPendingAsync(1,20,default,marker)).Items);
        }finally{
            await db.ExecuteAsync("DELETE FROM moderation_batches WHERE id=@batch;DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id=@id;DELETE FROM restaurants WHERE id=@id",new{id,batch=batch.Id});
        }
    }

    [Fact]
    public async Task AdminMembersMapUnsignedCountsAndUtcDatesIncludingNullLockout()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();
        await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var accounts=services.GetRequiredService<IMemberAccounts>();
        var email=Guid.NewGuid().ToString("N")+"@example.invalid";
        await accounts.RegisterAsync(new("Sprint48 member",email,"SafeS48!"+Guid.NewGuid().ToString("N"),true,"pt-BR"),default);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var id=await db.ExecuteScalarAsync<string>("SELECT id FROM member_users WHERE email=@email",new{email});
        try {
            var member=Assert.Single(await accounts.ListMembersAdminAsync(1,20,email,default));
            Assert.Equal(id,member.Id);Assert.Null(member.LockoutUntil);Assert.Equal(TimeSpan.Zero,member.CreatedAt.Offset);
            await db.ExecuteAsync("UPDATE member_users SET failed_attempts=7,lockout_until='2026-11-01 12:00:00' WHERE id=@id",new{id});
            member=Assert.Single(await accounts.ListMembersAdminAsync(1,20,email,default));
            Assert.Equal(7,member.FailedAttempts);Assert.Equal(new DateTimeOffset(2026,11,1,12,0,0,TimeSpan.Zero),member.LockoutUntil);
        }finally{await db.ExecuteAsync("DELETE FROM member_users WHERE id=@id",new{id});}
    }

    [Fact]
    public async Task RankingRecalculationIsAtomicAndSerialized()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();
        await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var ranking=services.GetRequiredService<ICuiaService>();
        await ranking.RecalculateRankingsAsync(default);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var before=(await db.QueryAsync<string>("SELECT CONCAT(restaurant_id,':',category_key,':',rank_position,':',score) FROM cuia_rankings ORDER BY restaurant_id,category_key")).ToArray();
        Assert.NotEmpty(before);
        var trigger="fixture_s48_"+Guid.NewGuid().ToString("N")[..12];
        try {
            await db.ExecuteAsync($"CREATE TRIGGER `{trigger}` BEFORE INSERT ON cuia_rankings FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='fixture rollback'");
            await Assert.ThrowsAsync<MySqlException>(()=>ranking.RecalculateRankingsAsync(default));
            var after=(await db.QueryAsync<string>("SELECT CONCAT(restaurant_id,':',category_key,':',rank_position,':',score) FROM cuia_rankings ORDER BY restaurant_id,category_key")).ToArray();
            Assert.Equal(before,after);
        }finally{await db.ExecuteAsync($"DROP TRIGGER IF EXISTS `{trigger}`");}
        await Task.WhenAll(ranking.RecalculateRankingsAsync(default),ranking.RecalculateRankingsAsync(default));
        Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT category_key,rank_position FROM cuia_rankings GROUP BY category_key,rank_position HAVING COUNT(*)>1) d"));
    }
}
