using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint57CulinaryTests
{
    [Fact] public async Task VideoTopicAloneDoesNotClaimRestaurantRelationship()
    {
        using var root=(ServiceProvider)ServiceContainer.Build();using var scope=root.CreateScope();var sp=scope.ServiceProvider;
        await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var place=await db.QueryFirstAsync("SELECT id,name FROM restaurants WHERE status='published' LIMIT 1");
        var url="https://www.youtube.com/watch?v="+Guid.NewGuid().ToString("N")[..11];
        try
        {
            var id=await db.ExecuteScalarAsync<long>("INSERT INTO culinary_videos(title,author_name,platform,canonical_url,related_topic,summary,attribution_kind,url_hash,observed_at) VALUES('Fixture57','Fixture','YouTube',@url,@topic,'Not the restaurant recipe','traditional_similar',SHA2(@url,256),UTC_TIMESTAMP(6));SELECT LAST_INSERT_ID()",new{url,topic=(string)place.name});
            var dossier=await sp.GetRequiredService<IPlaceDossier>().ForRestaurantAsync((long)place.id,default);
            Assert.DoesNotContain(dossier.Videos,v=>v.Id==id);
        }
        finally{await db.ExecuteAsync("DELETE FROM culinary_videos WHERE canonical_url=@url",new{url});}
    }
    [Fact] public async Task QueueDeduplicatesAcrossScopesAndFindUsesAllDishTerms()
    {
        using var root=(ServiceProvider)ServiceContainer.Build();using var scope=root.CreateScope();var sp=scope.ServiceProvider;
        await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var research=sp.GetRequiredService<ICulinaryResearch>();var token=Guid.NewGuid().ToString("N");var url="https://example.org/"+token;
        await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            await research.EnqueueAsync([url,url],"hambúrguer de camarão",default);
            using(var second=root.CreateScope())await second.ServiceProvider.GetRequiredService<ICulinaryResearch>().EnqueueAsync([url],"hambúrguer de camarão",default);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM culinary_tasks WHERE url=@url",new{url}));
            var record=new CulinaryReference(0,"recipe_reference","Hambúrguer de camarão",url,"Autor","Referência",["450 g camarão"],null,null,null,null,[],DateTime.UtcNow,null,"Receita do autor",null);
            await db.ExecuteAsync("INSERT INTO culinary_references(url_hash,canonical_url,kind,title,topic,search_text,body,observed_at) VALUES(SHA2(@url,256),@url,'recipe_reference','Hambúrguer de camarão','camarão','hambúrguer camarão',@body,UTC_TIMESTAMP(6))",new{url,body=System.Text.Json.JsonSerializer.Serialize(record)});
            Assert.Contains(await research.FindAsync("receita hambúrguer de camarão em Belém",default),r=>r.Url==url);
            Assert.DoesNotContain(await research.FindAsync("hambúrguer de peixe",default),r=>r.Url==url);
            var cuia=sp.GetRequiredService<ICuiaAiService>();
            var answer=await cuia.AskAsync(new("Onde compro os ingredientes?",SearchWeb:false,PreviousQuestions:["Quero hambúrguer de camarão em Belém","Tem receita publicada?","Mostre um vídeo"]),default);
            Assert.Contains(answer.Sources,r=>r.Url==url);
            Assert.Contains("pesquisa manual",answer.AnswerMarkdown);
            Assert.DoesNotContain("Wikipédia",answer.AnswerMarkdown);
        }
        finally{await db.ExecuteAsync("DELETE FROM culinary_tasks WHERE url=@url;DELETE FROM culinary_references WHERE canonical_url=@url",new{url});}
    }
}
