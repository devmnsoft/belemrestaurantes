using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class RecipeEditorialTests
{
    private static ParaRecipe Sample(string marker)=>new(){Slug="fixture-recipe-"+marker,Title="Receita "+marker,Subtitle="Adaptação de homologação",Story="Conteúdo temporário de teste, removido ao final.",Ingredients=["200 g de cupuaçu "+marker],Steps=["Separar os ingredientes.","Preparar com cuidado.","Servir."],PrepMinutes=10,CookMinutes=5};
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    [Fact]
    public async Task DraftPublicationSearchVersionAndRestaurantLinksRespectPublicVisibility()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var recipes=scope.ServiceProvider.GetRequiredService<IParaRecipes>();var read=scope.ServiceProvider.GetRequiredService<IRestaurantReadRepository>();
        var marker=Guid.NewGuid().ToString("N");var r=Sample(marker);long restaurantId=0;
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        try
        {
            await recipes.SaveAsync(r,"fixture-admin",default);
            Assert.Null(await recipes.GetAsync(r.Slug,false,default));Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/receitas/"+r.Slug)).StatusCode);
            Assert.Empty((await recipes.ListAsync(new(Query:marker),false,default)).Items);
            r.Status="published";r.Title="<script>alert('"+marker+"')</script>";await recipes.SaveAsync(r,"fixture-admin",default);
            var html=await client.GetStringAsync("/receitas/"+r.Slug);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<script>alert",html);Assert.Contains("application/ld+json",WebUtility.HtmlDecode(html));Assert.Contains("\\u003Cscript\\u003E",html);
            Assert.Single((await recipes.ListAsync(new(Query:marker,Quick:true),false,default)).Items);
            Assert.Single((await recipes.ListAsync(new(Query:"cupuaçu "+marker),false,default)).Items);
            var stale=Sample(marker);stale.Id=r.Id;stale.Version=1;await Assert.ThrowsAsync<ArgumentException>(()=>recipes.SaveAsync(stale,"fixture-admin",default));
            restaurantId=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new("recipe-restaurant-"+marker,"Casa de homologação "+marker,null,"Belém","Restaurante","Rua de homologação",null,null,"https://example.org/fixture",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(restaurantId,"fixture-admin",default));
            r.Title="Receita "+marker;r.RestaurantIds=[restaurantId];await recipes.SaveAsync(r,"fixture-admin",default);
            Assert.Single(await recipes.ForRestaurantAsync(restaurantId,default));Assert.Single((await recipes.ListAsync(new(RestaurantId:restaurantId),false,default)).Items);
            Assert.Single((await recipes.ListAsync(new(Query:"Casa de homologação "+marker),false,default)).Items);
            html=await client.GetStringAsync("/receitas/"+r.Slug);Assert.Contains("Casa de homologa",WebUtility.HtmlDecode(html));await Snapshot("recipe-sample",html);
            html=await client.GetStringAsync("/estabelecimento/"+restaurantId);Assert.Contains(r.Slug,html);
            var moderation=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();var archive=await moderation.PrepareAsync("fixture-admin","archive","Fim da homologação",[restaurantId],false,new(Stage:"published"),default);await moderation.ExecuteAsync(archive.Id,"fixture-admin",default);
            Assert.Null(await read.GetPublishedAsync(restaurantId,default));Assert.Empty((await recipes.GetAsync(r.Slug,false,default))!.RestaurantIds);
            r.Status="archived";r.RestaurantIds=[];await recipes.SaveAsync(r,"fixture-admin",default);Assert.Null(await recipes.GetAsync(r.Slug,false,default));
            Assert.Equal(4,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM para_recipe_versions WHERE recipe_id=@id",new{id=r.Id}));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM para_recipes WHERE slug=@slug",new{slug=r.Slug});
            await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject='fixture-admin' AND ((entity_type='recipe' AND entity_id=@id) OR (entity_type='restaurant' AND entity_id=@restaurantId))",new{id=r.Id,restaurantId});
            await db.ExecuteAsync("DELETE FROM moderation_batches WHERE actor='fixture-admin' AND selection_json LIKE @term",new{term="%"+restaurantId+"%"});
            if(restaurantId>0){await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@restaurantId;DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@restaurantId;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@restaurantId;DELETE FROM restaurants WHERE id=@restaurantId",new{restaurantId});}
        }
    }
    [Fact]
    public async Task EditorCanPublishThroughCsrfFormAndCannotOperateGlobalConsole()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role",AdminRoles.Editor);
        var r=Sample(Guid.NewGuid().ToString("N"));await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/admin/operacao")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/admin/receitas/salvar",new FormUrlEncodedContent([]))).StatusCode);
            var html=await client.GetStringAsync("/admin/receitas/editar");await Snapshot("admin-recipe-edit",html);
            var fields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"Slug",r.Slug},{"Title",r.Title},{"Subtitle",r.Subtitle},{"Category",r.Category},{"Course",r.Course},{"City",r.City},{"Difficulty",r.Difficulty},{"Servings","4"},{"PrepMinutes","10"},{"CookMinutes","5"},{"WaitMinutes","0"},{"Story",r.Story!},{"ingredientsText",string.Join('\n',r.Ingredients)},{"stepsText",string.Join('\n',r.Steps)},{"Kind","inspired"},{"Status","published"},{"Icon","cuia"},{"Tone","forest"}};
            var response=await client.PostAsync("/admin/receitas/salvar",new FormUrlEncodedContent(fields));Assert.True(response.StatusCode==HttpStatusCode.Redirect,await response.Content.ReadAsStringAsync());
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IParaRecipes>().GetAsync(r.Slug,false,default));
            html=await client.GetStringAsync("/admin/receitas");await Snapshot("admin-receitas",html);
        }
        finally
        {var id=await db.ExecuteScalarAsync<long?>("SELECT id FROM para_recipes WHERE slug=@slug",new{slug=r.Slug});await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject='fixture-admin' AND entity_type='recipe' AND entity_id=@id",new{id});await db.ExecuteAsync("DELETE FROM para_recipes WHERE slug=@slug",new{slug=r.Slug});}
    }
    [Fact]
    public async Task ApprovalPublicationAndArchiveHaveDistinctAuditedStages()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var marker=Guid.NewGuid().ToString("N");var id=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new("stages-"+marker,"Teste de etapas "+marker,null,"Belém","Restaurante","Rua de homologação",null,null,"https://example.org/fixture",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
        var service=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();var read=scope.ServiceProvider.GetRequiredService<IRestaurantReadRepository>();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            var invalid=await service.PrepareAsync("fixture-stages","publish",null,[id],false,new(),default);var result=await service.ExecuteAsync(invalid.Id,"fixture-stages",default);Assert.Equal("ignored",Assert.Single(result!.Results!).Outcome);
            var approve=await service.PrepareAsync("fixture-stages","approve_only",null,[id],false,new(),default);result=await service.ExecuteAsync(approve.Id,"fixture-stages",default);Assert.Equal("applied",Assert.Single(result!.Results!).Outcome);Assert.Null(await read.GetPublishedAsync(id,default));Assert.Equal("approved",await db.ExecuteScalarAsync<string>("SELECT moderation_stage FROM restaurants WHERE id=@id",new{id}));
            var publish=await service.PrepareAsync("fixture-stages","publish",null,[id],false,new(Stage:"approved"),default);result=await service.ExecuteAsync(publish.Id,"fixture-stages",default);Assert.NotNull(await read.GetPublishedAsync(id,default));Assert.Equal(result!.Results, (await service.ExecuteAsync(publish.Id,"fixture-stages",default))!.Results);
            var archive=await service.PrepareAsync("fixture-stages","archive","Revisão de homologação",[id],false,new(Stage:"published"),default);await service.ExecuteAsync(archive.Id,"fixture-stages",default);Assert.Null(await read.GetPublishedAsync(id,default));Assert.Equal("archived",await db.ExecuteScalarAsync<string>("SELECT moderation_stage FROM restaurants WHERE id=@id",new{id}));
        }
        finally{await db.ExecuteAsync("DELETE FROM moderation_batches WHERE actor='fixture-stages';DELETE FROM audit_events WHERE actor_subject='fixture-stages';DELETE FROM source_observations WHERE restaurant_id=@id;DELETE FROM restaurant_enrichment_queue WHERE restaurant_id=@id;DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id;DELETE FROM restaurants WHERE id=@id",new{id});}
    }
    private static async Task Snapshot(string name,string html)
    {var folder=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(folder is not null){Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,name+".html"),html);}}
}
