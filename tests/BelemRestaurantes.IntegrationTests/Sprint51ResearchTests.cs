using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint51ResearchTests
{
    [Fact] public async Task ExternalAutoPublicationUsesAuditedTransitionAndCanBeReversedWithoutPromotion()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);var marker="fixture51-"+Guid.NewGuid().ToString("N");
        var p=new ExtractedRestaurantData{Name=marker,City="Belém",Address="Rua de Teste, 123, Belém",Website="https://example.org/"+marker,SourceUrl="https://example.org/"+marker,StructuredIdentity=true,ExplicitCity=true,DeclaredWebsite=true,ConfidenceScore=.95};
        Assert.Null(AutoPublicationRules.BlockReason(p,new(){Status="active",SourceType="structured_data"},new(MatchOutcome.New,null,1,"new")));
        var write=await services.GetRequiredService<IRestaurantIngestionRepository>().UpsertWithOutcomeAsync(new DiscoveredPlace(marker,p.Name,null,"Belém","Restaurante",p.Address,null,null,p.SourceUrl,p.Website,null,null,[]),"crawler:fixture51",DateTimeOffset.UtcNow,default);
        var moderation=services.GetRequiredService<IModerationRepository>();await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);var id=write.Id;
        try
        {
            Assert.True(write.Created);Assert.True(await moderation.ApproveAsync(id,"system:external-auto-publication",default));Assert.False(await moderation.ApproveAsync(id,"system:external-auto-publication",default));
            Assert.Equal("ficha",await db.ExecuteScalarAsync<string>("SELECT catalog_tier FROM restaurants WHERE id=@id",new{id}));Assert.Equal("published",await db.ExecuteScalarAsync<string>("SELECT status FROM restaurants WHERE id=@id",new{id}));
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE entity_type='restaurant' AND entity_id=@id AND actor_subject='system:external-auto-publication' AND action='restaurant.approve'",new{id}));
            Assert.True(await moderation.SuspendAsync(id,"fixture51","Reversão de publicação de teste",default));Assert.Null(await services.GetRequiredService<IRestaurantReadRepository>().GetPublishedAsync(id,default));
        }
        finally{await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id=@id;DELETE FROM restaurants WHERE id=@id",new{id});}
    }
    [Fact] public async Task ResearchJobsClaimOncePersistOutcomeAndRespectPausedTarget()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker="fixture51-"+Guid.NewGuid().ToString("N");var target=await db.ExecuteScalarAsync<long>("INSERT INTO discovery_targets(city,normalized_city,category,enabled,next_run_at) VALUES('Belém','belem',@marker,0,UTC_TIMESTAMP(6));SELECT LAST_INSERT_ID()",new{marker});
        var id=await db.ExecuteScalarAsync<long>("INSERT INTO research_jobs(target_id,run_day,city,query_text) VALUES(@target,UTC_DATE(),'Belém',@marker);SELECT LAST_INSERT_ID()",new{target,marker});var repo=services.GetRequiredService<IResearchJobs>();
        try
        {
            Assert.DoesNotContain(await repo.RecentAsync(default),r=>r.Id==id&&r.State!="pending");
            Assert.Null(await repo.ClaimAsync(default,target));
            await db.ExecuteAsync("UPDATE discovery_targets SET enabled=1 WHERE id=@target",new{target});
            var job=await repo.ClaimAsync(default,target);Assert.NotNull(job);Assert.Equal(id,job.Id);Assert.Null(await repo.ClaimAsync(default,target));
            await repo.CompleteAsync(id,"ok",8,2,default);var row=await db.QuerySingleAsync("SELECT state,last_status,source_count,queued_count FROM research_jobs WHERE id=@id",new{id});Assert.Equal("completed",(string)row.state);Assert.Equal("ok",(string)row.last_status);Assert.Equal(8,(int)row.source_count);Assert.Equal(2,(int)row.queued_count);
        }
        finally{await db.ExecuteAsync("DELETE FROM research_jobs WHERE id=@id;DELETE FROM discovery_targets WHERE id=@target",new{id,target});}
    }
}
