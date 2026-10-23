using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint48ReferenceTests
{
    [Fact]
    public async Task MenuAnswerUsesReviewedExternalEvidenceWithoutCreatingARestaurant()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var repo=services.GetRequiredService<IPlaceReferences>();var name="Restaurante "+Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow;
        var id=await repo.ObserveAsync(new(0,name,"Belém","https://example.org/"+Guid.NewGuid().ToString("N"),"editorial_reference","Publicação de teste",now,now.AddDays(1),["Prato documentado na fonte"]),default);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            await repo.ReviewAsync(id,"approved","fixture-s48",default);
            var answer=await services.GetRequiredService<ICuiaAiService>().AskAsync(new("Me diga os pratos do "+name,SearchWeb:true),default);
            Assert.Equal("reference_answer",answer.GenerationStatus);Assert.Equal("indexed_sources",answer.SearchStatus);
            Assert.Contains("Prato documentado na fonte",answer.AnswerMarkdown);Assert.Contains("não confirmam um cardápio oficial",answer.AnswerMarkdown);
            Assert.Single(answer.Sources);Assert.Single(answer.ExternalSuggestions);Assert.Empty(answer.Recommendations);
            var follow = await services.GetRequiredService<ICuiaAiService>().AskAsync(new("E como posso confirmar o cardápio atual?", PreviousQuestions:["Me diga os pratos do "+name]), default);
            Assert.Equal("reference_answer", follow.GenerationStatus); Assert.Contains(name, follow.AnswerMarkdown);
            Assert.Contains("canal oficial", follow.AnswerMarkdown); Assert.Single(follow.Sources);

            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurants WHERE name=@name",new{name}));
        }finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='place_reference' AND entity_id=@id;DELETE FROM place_reference_index WHERE id=@id",new{id});}
    }

    [Fact]
    public async Task ReferencesNeedReviewExpireAndNeverPublishPlacesOrReopenUnchangedDecisions()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var repository=services.GetRequiredService<IPlaceReferences>();var name="Fonte "+Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow;
        var source=new PlaceReference(0,name,"Belém","https://example.org/"+Guid.NewGuid().ToString("N"),"editorial_reference","Fonte de integração",now,now.AddDays(1),["Prato citado"],"Endereço informado",["javascript:alert(1)","https://www.youtube.com/watch?v=test"]);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);var id=await repository.ObserveAsync(source,default);
        try{
            Assert.Empty(await repository.FindAsync("Me diga os pratos do "+name,null,default));
            Assert.True(await repository.ReviewAsync(id,"approved","fixture-s48",default));
            var found=Assert.Single(await repository.FindAsync("Me diga os pratos do "+name,null,default));
            Assert.Single(found.VideoLinks!);Assert.Contains("youtube.com",found.VideoLinks![0]);
            await repository.ObserveAsync(source,default);Assert.Single(await repository.FindAsync(name,null,default));
            await repository.ObserveAsync(source with{MenuItems=["Novo fato"]},default);Assert.Empty(await repository.FindAsync(name,null,default));
            Assert.True(await repository.ReviewAsync(id,"rejected","fixture-s48",default));
            await repository.ObserveAsync(source with{MenuItems=["Novo fato"]},default);
            Assert.Equal("rejected",await db.ExecuteScalarAsync<string>("SELECT status FROM place_reference_index WHERE id=@id",new{id}));
            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurants WHERE name=@name",new{name}));
            await db.ExecuteAsync("UPDATE place_reference_index SET status='approved',expires_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE) WHERE id=@id",new{id});
            Assert.Empty(await repository.FindAsync(name,null,default));
        }finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='place_reference' AND entity_id=@id",new{id});await db.ExecuteAsync("DELETE FROM place_reference_index WHERE id=@id",new{id});}
    }

    [Fact]
    public async Task CursorAndJobCompletionCommitTogetherAndScheduleContinuation()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker="s48-page-"+Guid.NewGuid().ToString("N");
        var target=await db.ExecuteScalarAsync<long>("INSERT INTO discovery_targets(city,normalized_city,category,enabled,next_run_at) VALUES('Belém',@marker,'regional',FALSE,UTC_TIMESTAMP(6)); SELECT LAST_INSERT_ID();",new{marker});
        try{
            var job=await db.ExecuteScalarAsync<long>("INSERT INTO discovery_jobs(target_id,city,category,state,locked_by,locked_until) VALUES(@target,'Belém','regional','running','fixture',DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 4 MINUTE));SELECT LAST_INSERT_ID();",new{target});
            await services.GetRequiredService<IDiscoveryQueue>().CompletePageAsync(job,new(50,true,DateTimeOffset.UtcNow,180),default);
            var row=await db.QuerySingleAsync("SELECT result_offset,result_more,result_source_total,next_run_at FROM discovery_targets WHERE id=@target",new{target});
            Assert.Equal(50,(int)row.result_offset);Assert.True((bool)row.result_more);Assert.Equal(180,(int)row.result_source_total);
            Assert.InRange((DateTime)row.next_run_at,DateTime.UtcNow.AddMinutes(4),DateTime.UtcNow.AddMinutes(6));
            Assert.Equal("succeeded",await db.ExecuteScalarAsync<string>("SELECT state FROM discovery_jobs WHERE id=@job",new{job}));
        }finally{await db.ExecuteAsync("DELETE FROM discovery_jobs WHERE target_id=@target;DELETE FROM discovery_targets WHERE id=@target",new{target});}
    }
}
