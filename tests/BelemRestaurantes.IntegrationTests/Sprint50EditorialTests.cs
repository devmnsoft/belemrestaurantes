using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Infrastructure.Partner;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint50EditorialTests
{
    [Theory][InlineData("not_configured")][InlineData("empty")][InlineData("unavailable")][InlineData("quota_exhausted")]
    public async Task SsrResponseDoesNotRenderSourcesHeadingOrListWithoutSources(string status)
    {
        using var app=BulkAndVisitTests.Factory().WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IWebSearch>(_=>new EmptySearch(status))));
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/cuia-ia?searchWeb=true&q="+Uri.EscapeDataString("Pratos do Restaurante Inexistente")));
        Assert.DoesNotContain("Fontes da pesquisa externa",html);Assert.DoesNotContain("<ol>",html);Assert.Contains(BelemRestaurantes.Application.Formatting.CuiaSearchPresentation.Message(status),html);Assert.Contains("Explorar roteiros",html);
    }
    private sealed class EmptySearch(string status):IWebSearch{public Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)=>Task.FromResult(new WebSearchResult(status,[]));}
    [Fact] public async Task ReviewedSeedContainsSevenRealGuidesAndRerunNeverOverwritesEdits()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var publicRepo=services.GetRequiredService<IMobilityTrailService>();var all=await publicRepo.ListTrailsAsync(default);var seeded=all.Where(t=>t.Editorial is not null).ToArray();Assert.True(seeded.Length>=7);
        Assert.All(seeded,t=>{Assert.InRange(t.Editorial!.Stops.Count,3,5);Assert.All(t.Editorial.Stops,s=>Assert.StartsWith("https://",s.SourceUrl));});
        var count=seeded.Length;await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);Assert.Equal(count,(await publicRepo.ListTrailsAsync(default)).Count(t=>t.Editorial is not null));
    }
    [Fact] public async Task DraftPublicationVersionHistoryAndReviewQueueUseRealTransactions()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var slug="fixture50-"+Guid.NewGuid().ToString("N");var id=await db.ExecuteScalarAsync<int>("INSERT INTO gastronomic_trails(slug,title,region,description,is_active) VALUES(@slug,'Fixture editorial','Belém','Descrição editorial de teste',FALSE);SELECT LAST_INSERT_ID();",new{slug});
        var editor=services.GetRequiredService<IEditorialTrails>();var read=services.GetRequiredService<IMobilityTrailService>();var guide=new TrailEditorial("Visitantes", "Belém","urban","Confira o acesso antes de ir.","Acessibilidade deve ser confirmada.",Enumerable.Range(1,3).Select(i=>new EditorialTrailStop("Lugar "+i,"Belém","Orientação editorial de teste.","https://example.org/"+i,"Fonte de teste")).ToArray());
        try{
            Assert.True(await editor.SaveDraftAsync(id,"Título revisado","Descrição editorial de teste",3,guide,1,"fixture50",default));Assert.Null(await read.GetTrailBySlugAsync(slug,default));
            Assert.False(await editor.SaveDraftAsync(id,"Edição antiga","Descrição editorial de teste",3,guide,1,"fixture50",default));
            Assert.True(await editor.SetPublishedAsync(id,true,2,"fixture50",default));Assert.NotNull(await read.GetTrailBySlugAsync(slug,default));
            await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);Assert.Equal("Título revisado",(await read.GetTrailBySlugAsync(slug,default))!.Title);
            await db.ExecuteAsync("UPDATE gastronomic_trails SET reviewed_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 91 DAY) WHERE id=@id",new{id});await services.GetRequiredService<EditorialReviewScheduler>().EnqueueDueAsync(default);await services.GetRequiredService<EditorialReviewScheduler>().EnqueueDueAsync(default);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM editorial_review_queue WHERE trail_id=@id AND state='pending'",new{id}));
            Assert.True(await editor.SetPublishedAsync(id,false,3,"fixture50",default));Assert.Null(await read.GetTrailBySlugAsync(slug,default));
            Assert.Equal(3,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM editorial_trail_revisions WHERE trail_id=@id",new{id}));
            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM editorial_review_queue WHERE trail_id=@id AND state='pending'",new{id}));
        }finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='trail' AND entity_id=@key;DELETE FROM gastronomic_trails WHERE id=@id",new{id,key=id.ToString()});}
    }
    [Fact] public async Task EditorSavesThreeStepsWithoutRequiringEmptyOptionalRows()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var slug="form50-"+Guid.NewGuid().ToString("N");var id=await db.ExecuteScalarAsync<int>("INSERT INTO gastronomic_trails(slug,title,region,description,is_active) VALUES(@slug,'Fixture formulário','Belém','Descrição editorial de teste',FALSE);SELECT LAST_INSERT_ID();",new{slug});
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        try{
            var html=await client.GetStringAsync("/admin/roteiros/"+id);
            var token=System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var fields=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Version","1"},{"Title","Título salvo pelo formulário"},{"Description","Descrição editorial de teste"},{"DurationHours","3"},{"Audience","Visitantes"},{"Origin","Belém"},{"Mode","urban"},{"BeforeYouGo","Confira as condições antes de ir."},{"Accessibility","Confirme acessibilidade diretamente."}};
            for(var i=0;i<5;i++){fields[$"Stops[{i}].Name"]=i<3?"Etapa "+i:"";fields[$"Stops[{i}].City"]="Belém";fields[$"Stops[{i}].Summary"]=i<3?"Uma orientação editorial de teste.":"";fields[$"Stops[{i}].SourceName"]=i<3?"Fonte de teste":"";fields[$"Stops[{i}].SourceUrl"]=i<3?"https://example.org/"+i:"";}
            var response=await client.PostAsync("/admin/roteiros/"+id,new FormUrlEncodedContent(fields));Assert.True(response.StatusCode==HttpStatusCode.Redirect,await response.Content.ReadAsStringAsync());
            var row=await scope.ServiceProvider.GetRequiredService<IEditorialTrails>().GetForEditingAsync(id,default);Assert.Equal(3,row!.Editorial!.Stops.Count);Assert.Equal("draft",row.EditorialState);
        }finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='trail' AND entity_id=@key;DELETE FROM gastronomic_trails WHERE id=@id",new{id,key=id.ToString()});}
    }
    [Fact] public async Task PublicCrossingsAndAdminEditorRenderAndRequireAntiforgery()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var trail=(await scope.ServiceProvider.GetRequiredService<IMobilityTrailService>().ListTrailsAsync(default)).First(t=>t.Editorial is not null);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        foreach(var path in new[]{"/travessias","/roteiros","/roteiros/"+trail.Slug,"/admin/roteiros","/admin/roteiros/"+trail.Id})
        {
            var response=await client.GetAsync(path);var html=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,html[..Math.Min(html.Length,1600)]);
            var output=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,path.Trim('/').Replace('/','-')+".html"),html);}
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/admin/roteiros/"+trail.Id,new FormUrlEncodedContent([]))).StatusCode);
        using var guest=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});guest.DefaultRequestHeaders.Add("Test-Role",BelemRestaurantes.Domain.Identity.AdminRoles.Operator);Assert.Equal(HttpStatusCode.Forbidden,(await guest.GetAsync("/admin/roteiros")).StatusCode);
    }
}
