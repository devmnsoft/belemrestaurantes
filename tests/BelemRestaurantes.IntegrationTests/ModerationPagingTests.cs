using System.Diagnostics;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using Dapper;
using System.Net;
using System.Text.RegularExpressions;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class ModerationPagingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PageSlicesPreserveFiltersOrderingEligibilityAndReturnContext()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var bulk=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();
        var ingest=scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>();
        var inbox=scope.ServiceProvider.GetRequiredService<IModerationRepository>();
        var marker="paging-"+Guid.NewGuid().ToString("N");var ids=new List<long>();var batches=new List<string>();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            for(var i=0;i<34;i++)ids.Add(await ingest.UpsertDiscoveredAsync(new(marker+i,marker+"-"+i.ToString("D2"),null,"Belém","Restaurante","Rua teste "+i,null,null,"https://example.org/source",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default));
            await db.ExecuteAsync("UPDATE restaurants SET created_at='2026-01-01' WHERE id IN @ids",new{ids});
            await db.ExecuteAsync("INSERT INTO moderation_queue(item_type,item_id,reason,detail,status,created_at) VALUES('restaurant',@id,'PossibleDuplicate','Fixture de duplicidade','pending',UTC_TIMESTAMP(6))",new{id=ids[0]});
            await db.ExecuteAsync("UPDATE restaurants SET address=NULL WHERE id=@id",new{id=ids[1]});
            await db.ExecuteAsync("UPDATE restaurants SET moderation_stage='approved' WHERE id=@id",new{id=ids[2]});
            await db.ExecuteAsync("UPDATE restaurants SET status='published',moderation_stage='published' WHERE id=@id",new{id=ids[3]});
            await db.ExecuteAsync("INSERT INTO source_observations(restaurant_id,provider,external_id,last_seen_at) VALUES(@id,'latest_fixture',@marker,DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE))",new{id=ids[0],marker});
            var f=new ModerationFilter(Term:marker,PageSize:30);
            var first=await bulk.ListAsync(f,1,default);var second=await bulk.ListAsync(f,2,default);
            Assert.Equal(32,first.TotalCount);Assert.Equal(30,first.Items.Count);Assert.Equal(2,second.Items.Count);
            Assert.Empty(first.Items.Select(r=>r.Id).Intersect(second.Items.Select(r=>r.Id)));
            Assert.Equal(first.Items.Select(r=>r.Id).Order(),first.Items.Select(r=>r.Id));
            Assert.Equal(first.Items.Select(r=>r.Id),(await bulk.ListAsync(f,1,default)).Items.Select(r=>r.Id));
            Assert.Equal(2,(await bulk.ListAsync(f,int.MaxValue,default)).Page);
            Assert.Equal(30,(await bulk.ListAsync(f with{PageSize=31},1,default)).PageSize);
            var duplicate=await bulk.ListAsync(f with{Duplicate="yes"},1,default);Assert.Single(duplicate.Items);Assert.Equal(ids[0],duplicate.Items[0].Id);
            Assert.Equal(30,(await bulk.ListAsync(f with{Completeness="eligible"},1,default)).TotalCount);
            Assert.Equal(2,(await bulk.ListAsync(f with{Completeness="incomplete"},1,default)).TotalCount);
            Assert.Equal(ids[2],Assert.Single((await bulk.ListAsync(f with{Stage="approved"},1,default)).Items).Id);
            Assert.Equal(ids[3],Assert.Single((await bulk.ListAsync(f with{Stage="published"},1,default)).Items).Id);
            var source=Assert.Single((await bulk.ListAsync(f with{Source="latest_fixture"},1,default)).Items);Assert.Equal(ids[0],source.Id);Assert.Equal("latest_fixture",source.Source);
            Assert.Empty((await bulk.ListAsync(f with{City="Cidade inexistente"},1,default)).Items);
            Assert.Equal(first.Items.Select(r=>r.Id),(await bulk.ListAsync(f with{Sort="name; DROP TABLE restaurants"},1,default)).Items.Select(r=>r.Id));
            var inboxPage=await inbox.ListPendingAsync(int.MaxValue,30,default,marker);
            Assert.Equal(2,inboxPage.Page);Assert.Equal(33,inboxPage.TotalCount);Assert.Equal(3,inboxPage.Items.Count);

            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
            var html=WebUtility.HtmlDecode(await client.GetStringAsync("/admin/moderacao?Term="+marker+"&page=2&PageSize=30"));
            Assert.Contains("review-page-top",html);Assert.Contains("review-page-bottom",html);Assert.Contains("data-selection-key",html);
            var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var prepared=await client.PostAsync("/admin/moderacao/preparar",new FormUrlEncodedContent(new Dictionary<string,string>{{"Term",marker},{"page","2"},{"PageSize","30"},{"operation","approve_only"},{"ids",second.Items[0].Id.ToString()},{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.OK,prepared.StatusCode);
            var confirm=WebUtility.HtmlDecode(await prepared.Content.ReadAsStringAsync());
            var batchId=Regex.Match(confirm,"name=\"id\" value=\"([^\"]+)\"").Groups[1].Value;Assert.True(Guid.TryParse(batchId,out _));batches.Add(batchId);
            var returnUrl=Regex.Match(confirm,"name=\"returnUrl\" value=\"([^\"]+)\"").Groups[1].Value;
            Assert.Contains("page=2",returnUrl);Assert.Contains(marker,returnUrl);
            token=Regex.Match(confirm,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var confirmed=await client.PostAsync("/admin/moderacao/confirmar",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",batchId},{"confirmed","true"},{"returnUrl",returnUrl},{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.OK,confirmed.StatusCode);
            Assert.Contains(returnUrl,WebUtility.HtmlDecode(await confirmed.Content.ReadAsStringAsync()));
            var replay=await client.PostAsync("/admin/moderacao/confirmar",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",batchId},{"confirmed","true"},{"returnUrl","https://evil.invalid"},{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            Assert.DoesNotContain("evil.invalid",await replay.Content.ReadAsStringAsync());
            Assert.Equal(BelemRestaurantes.Web.Models.ModerationNavigation.SelectionKey(f),BelemRestaurantes.Web.Models.ModerationNavigation.SelectionKey(f with{PageSize=120}));
            Assert.Equal(BelemRestaurantes.Web.Models.ModerationNavigation.SelectionKey(f),BelemRestaurantes.Web.Models.ModerationNavigation.SelectionKey(f with{Sort="name"}));
            var discovery=await client.GetStringAsync("/admin/discovery?term="+marker+"&page=2147483647");
            Assert.Contains("discovery-page-top",discovery);Assert.Contains("discovery-page-bottom",discovery);
            token=WebUtility.HtmlDecode(Regex.Match(discovery,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var singleApproval=await client.PostAsync($"/admin/discovery/{second.Items[1].Id}/approve",new FormUrlEncodedContent(new Dictionary<string,string>{{"term",marker},{"page","2"},{"__RequestVerificationToken",token}}));
            Assert.Equal(HttpStatusCode.Redirect,singleApproval.StatusCode);
            Assert.Contains(marker,singleApproval.Headers.Location!.ToString());Assert.Contains("page=2#revisao-cadastros",singleApproval.Headers.Location!.ToString());
        }
        finally
        {
            if(batches.Count>0)await db.ExecuteAsync("DELETE FROM moderation_batches WHERE id IN @batches",new{batches});
            if(ids.Count>0){await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids",new{ids});}
        }
    }
    [Theory]
    [InlineData("oldest",1)]
    [InlineData("oldest",100)]
    [InlineData("name",1)]
    [InlineData("recent",1)]
    [InlineData("city",1)]
    public async Task CatalogPagingReturnsBoundedOrderedResults(string sort,int page)
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();
        var bulk=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();
        await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
        var clock=Stopwatch.StartNew();
        var result=await bulk.ListAsync(new(Sort:sort),page,default);
        output.WriteLine($"sort={sort}; requestedPage={page}; page={result.Page}; total={result.TotalCount}; rows={result.Items.Count}; elapsedMs={clock.Elapsed.TotalMilliseconds:F1}");
        if(sort=="oldest"&&page==1)
        {
            await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            var plan=await db.QueryAsync("EXPLAIN SELECT r.id FROM restaurants r WHERE r.status='pending' AND r.moderation_stage='pending' ORDER BY r.created_at,r.id LIMIT 30");
            foreach(var row in plan)output.WriteLine($"paging index={row.key}; extra={row.Extra}");
        }
        Assert.InRange(result.Page,1,Math.Max(1,result.TotalPages));
        Assert.InRange(result.Items.Count,0,result.PageSize);
        Assert.Equal(result.Items.Count,result.Items.Select(r=>r.Id).Distinct().Count());
    }
}
