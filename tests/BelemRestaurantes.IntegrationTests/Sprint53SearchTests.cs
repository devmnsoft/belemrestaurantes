using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint53SearchTests
{
    [Fact]public async Task SearchCachePersistsEvidenceAndExpiresWithoutRawQuery()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var archive=services.GetRequiredService<ISearchArchive>();var key=Guid.NewGuid().ToString("N").PadLeft(64,'0');
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            Assert.Null(await archive.GetAsync(key,default));
            var observed=DateTimeOffset.UtcNow.AddHours(-1);
            await archive.SaveAsync(key,new("ok",[new("Fonte de culinária","https://example.org/culinaria","Referência pública",observed){SourceKind="search_snippet"}]){Provider="fixture"},default);
            var found=await archive.GetAsync(key,default);Assert.NotNull(found);Assert.Single(found.Sources);Assert.Equal(observed,found.Sources[0].RetrievedAt);Assert.Equal("fixture",found.Provider);
            await db.ExecuteAsync("UPDATE search_result_cache SET expires_at=DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 MINUTE) WHERE query_hash=@key",new{key});Assert.Null(await archive.GetAsync(key,default));
        }finally{await db.ExecuteAsync("DELETE FROM search_result_cache WHERE query_hash=@key",new{key});}
    }
    [Fact]public async Task PublicSearchRejectsPersonalDataAndShowsFederatedUi()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,(await client.GetAsync("/api/busca/fontes?q=pessoa%40example.org")).StatusCode);
        var html=await client.GetStringAsync("/busca?q=tacaca");Assert.Contains("data-search-evidence",html);Assert.Contains("federated-search.js",html);
    }
    [Fact]public async Task ConsecutiveRecipeQuestionsUsePublishedIngredientsWithoutCloudHallucination()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);var cuia=services.GetRequiredService<ICuiaAiService>();
        var recipes=services.GetRequiredService<IParaRecipes>();
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var slugs=new[]{("ci-tacaca-referencia","Tacacá de referência",new[]{"tucupi","peixe frito","jambu"}),("ci-manicoba-referencia","Maniçoba de referência",new[]{"maniva","pequi","carne de sol"})};
        var created=new Dictionary<string,bool>();
        foreach(var (slug,_,_) in slugs)created[slug]=await recipes.GetAsync(slug,false,default) is null;
        foreach(var (slug,title,ingredients) in slugs.Where(s => created[s.Item1]))
            await recipes.SaveAsync(new ParaRecipe{Slug=slug,Title=title,Subtitle="Fixture editorial do teste de referência local.",Ingredients=ingredients,Steps=["Cozinhar os ingredientes indicados.","Servir."],Status="published"},"fixture-admin",default);
        try
        {
            var first=await cuia.AskAsync(new("Quais ingredientes compõem o tacacá?",SearchWeb:true),default);
            Assert.Equal("reference_answer",first.GenerationStatus);Assert.Contains("tucupi",first.AnswerMarkdown,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("caldo de peixe",first.AnswerMarkdown,StringComparison.OrdinalIgnoreCase);
            var second=await cuia.AskAsync(new("E a maniçoba, como é preparada?",SearchWeb:true,PreviousQuestions:["Quais ingredientes compõem o tacacá?"]),default);
            Assert.Equal("reference_answer",second.GenerationStatus);Assert.Contains("maniva",second.AnswerMarkdown,StringComparison.OrdinalIgnoreCase);Assert.NotEmpty(second.Recipes);
        }
        finally
        {
            foreach(var slug in slugs.Where(s => created[s.Item1]).Select(s => s.Item1))
            {
                var id=await db.ExecuteScalarAsync<long?>("SELECT id FROM para_recipes WHERE slug=@slug",new{slug});
                if(id is not null){await db.ExecuteAsync("DELETE FROM para_recipe_versions WHERE recipe_id=@id",new{id});await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='recipe' AND entity_id=@id AND actor_subject='fixture-admin'",new{id});await db.ExecuteAsync("DELETE FROM para_recipes WHERE id=@id",new{id});}
            }
        }
    }
}
