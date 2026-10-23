using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Formatting;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint52ContentTests
{
    [Fact]public async Task CollectionIsAutomaticCanonicalAndSuspensionSurvivesRecollection()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);var repo=services.GetRequiredService<IExternalContent>();var marker="fixture52-"+Guid.NewGuid().ToString("N");
        var item=new ExternalContent{Title="Restaurante "+marker,City="Belém",Url="https://example.org/"+marker+"?utm_source=feed",Summary="Conteúdo do artigo que não deve ser copiado",VideoUrl="https://www.youtube.com/watch?v=fixture52",ObservedAt=DateTime.UtcNow,FactsJson=System.Text.Json.JsonSerializer.Serialize(new ExternalContentFacts(marker,"Rua Teste, 123",["Prato documentado"]))};
        var id=await repo.ObserveAsync(item,default);Assert.NotNull(id);await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            item.Url="https://example.org/"+marker;Assert.Equal(id,await repo.ObserveAsync(item,default));var saved=await repo.GetAsync(id.Value,default);Assert.NotNull(saved);Assert.Equal("published",saved.State);Assert.DoesNotContain("Conteúdo do artigo",saved.Summary);Assert.Equal("Rua Teste, 123",saved.Facts.Address);Assert.NotNull(saved.VideoUrl);
            item.FactsJson="{}";await repo.ObserveAsync(item,default);Assert.Equal("Rua Teste, 123",(await repo.GetAsync(id.Value,default))!.Facts.Address);
            Assert.Single(await repo.ListAsync(marker,"Belém",8,default));
            var answer=await services.GetRequiredService<ICuiaAiService>().AskAsync(new("Me diga os pratos do restaurante "+marker),default);Assert.Equal("collected_sources",answer.SearchStatus);Assert.Contains("Prato documentado",answer.AnswerMarkdown);Assert.Single(answer.Sources);
            Assert.True(await repo.SetStateAsync(id.Value,"suspended","fixture52",default));await repo.ObserveAsync(item,default);Assert.Null(await repo.GetAsync(id.Value,default));Assert.Empty(await repo.ListAsync(marker,null,8,default));Assert.Contains(item.Url,await repo.SuppressedUrlsAsync([item.Url+"?utm_source=again"],default));Assert.True(await repo.SetStateAsync(id.Value,"published","fixture52",default));Assert.NotNull(await repo.GetAsync(id.Value,default));
            Assert.Equal(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE entity_type='external_content' AND entity_id=@id AND action='external_content.state'",new{id}));
        }
        finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='external_content' AND entity_id=@id;DELETE FROM external_content WHERE id=@id",new{id});}
    }
    [Fact]public async Task ArticleMetadataAndVideoSchemaAreCollectedWithoutArticleBodyOrThirdPartyImage()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);var repo=services.GetRequiredService<IExternalContent>();var marker="fixture52-"+Guid.NewGuid().ToString("N");var url="https://example.org/"+marker;
        var html="""<meta content="article" property="og:type"><meta property="og:title" content="Passeio gastronômico pelo Pará"><meta property="og:description" content="Restaurantes, ilhas e sabores."><meta property="article:published_time" content="2026-10-20T10:00:00Z"><meta property="og:image" content="https://example.org/foto-sem-licenca.jpg"><script type="application/ld+json">{"@type":"VideoObject","embedUrl":"https://www.youtube.com/embed/fixture52"}</script><article>Texto integral da reportagem, que não deve ser republicado.</article>""";
        await repo.ObservePageAsync(html,url,"Belém",default);await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);var id=await db.ExecuteScalarAsync<long>("SELECT id FROM external_content WHERE url=@url",new{url});
        try{var item=await repo.GetAsync(id,default);Assert.NotNull(item);Assert.Equal("https://www.youtube.com/embed/fixture52",item.VideoUrl);Assert.DoesNotContain("Texto integral",item.Summary);Assert.Equal("2026-10-20",item.PublishedAt!.Value.ToString("yyyy-MM-dd"));Assert.DoesNotContain("foto-sem-licenca",ReferencePreview.Render(item));}
        finally{await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='external_content' AND entity_id=@id;DELETE FROM external_content WHERE id=@id",new{id});}
    }
    [Fact]public async Task PublicHubPreviewAndAdminPermissionsUseRealRoutes()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        var html=await client.GetStringAsync("/referencias");Assert.Contains("Prévia gerada pelo portal",html);Assert.Contains("reference-capture.js",html);
        var first=(await scope.ServiceProvider.GetRequiredService<IExternalContent>().ListAsync(null,null,1,default)).First();var response=await client.GetAsync($"/referencias/{first.Id}/previa.svg");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var svg=await response.Content.ReadAsStringAsync();Assert.Contains("Prévia do portal",svg);Assert.DoesNotContain("<script",svg);Assert.DoesNotContain("foreignObject",svg);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/admin/conteudos-externos")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/admin/conteudos-externos/{first.Id}/estado",new FormUrlEncodedContent([]))).StatusCode);
        using var guest=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});guest.DefaultRequestHeaders.Add("Test-Role",BelemRestaurantes.Domain.Identity.AdminRoles.Operator);Assert.Equal(HttpStatusCode.Forbidden,(await guest.GetAsync("/admin/conteudos-externos")).StatusCode);
    }
    [Fact]public void PreviewEscapesMarkupAndHasNoRemoteResources()
    {var svg=ReferencePreview.Render(new(){Title="Restaurante <script>alert(1)</script> & teste",Publisher="example.org",City="Belém"});Assert.DoesNotContain("<script>",svg);Assert.DoesNotContain("<image",svg);Assert.Contains("&lt;script&gt;",svg);}
}
