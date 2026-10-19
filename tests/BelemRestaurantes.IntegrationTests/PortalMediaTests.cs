using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class PortalMediaTests
{
    [Fact]
    public async Task HomepageIncludesVerifiedEditorialVideoAndKeyboardReadingControls()
    {
        using var app=BulkAndVisitTests.Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var html=await client.GetStringAsync("/");
        Assert.Contains("https://globoplay.globo.com/v/12465126/",html);
        Assert.Contains("data-reading-size",html);
        Assert.Contains("data-reading-contrast",html);
        Assert.Contains("/js/accessibility.js",html);
        Assert.DoesNotContain("<iframe",html);
    }

    [Fact]
    public async Task ExplicitSelectionAcceptsTwoThousandIdsWithoutModelBindingFailure()
    {
        using var app=BulkAndVisitTests.Factory().WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Moderation:BatchLimit"]="2000"})));
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        var marker="absent-batch-"+Guid.NewGuid().ToString("N");
        var html=await client.GetStringAsync("/admin/moderacao?Term="+marker);
        var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var values=Enumerable.Range(1,2000).Select(i=>new KeyValuePair<string,string>("ids",i.ToString())).ToList();
        values.AddRange([new("__RequestVerificationToken",token),new("Term",marker),new("operation","approve")]);
        var response=await client.PostAsync("/admin/moderacao/preparar",new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode); // empty filtered selection is handled normally
        var errorPage=await client.GetStringAsync(response.Headers.Location!);
        Assert.Contains("Selecione entre 1 e 2000",WebUtility.HtmlDecode(errorPage));
    }

    [Fact]
    public async Task CatalogUsesRealSchemaAndOffersReadingToolsVideosAndPagination()
    {
        using var app=BulkAndVisitTests.Factory();
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        var marker="midia-fixture-"+Guid.NewGuid().ToString("N");
        using var scope=app.Services.CreateScope();
        var id=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new(marker,marker,null,"Belém","Pizzaria","Rua de teste",null,null,"https://example.org/source",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            var response=await client.GetAsync("/admin/restaurants?query="+marker+"&page=2147483647");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var html=await response.Content.ReadAsStringAsync();
            Assert.Contains("Conforto de leitura",html);
            Assert.Contains("Gerenciar",html);
            Assert.Contains("/admin/midias?restaurantId="+id,html);
            Assert.Contains("/establishment/"+id,html);
            Assert.Contains("Páginas do catálogo",WebUtility.HtmlDecode(html));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id});
        }
    }

    [Fact]
    public async Task VideoIsValidatedReviewedAuditedAndOnlyThenDisplayedPublicly()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();
        var marker="media-fixture-"+Guid.NewGuid().ToString("N");
        var id=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new(marker,marker,null,"Belém","Pizzaria","Rua de teste",null,null,"https://example.org/source",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        async Task<HttpResponseMessage> Submit(string route,params(string,string)[] fields)
        {
            var html=await client.GetStringAsync("/admin/midias?restaurantId="+id);
            var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            return await client.PostAsync(route,new FormUrlEncodedContent(fields.Select(f=>new KeyValuePair<string,string>(f.Item1,f.Item2)).Append(new("__RequestVerificationToken",token))));
        }
        try
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(id,"fixture-admin",default));
            var fields=new[]{("restaurantId",id.ToString()),("url","https://www.youtube.com/watch?v=abcdefghijk"),("title",marker),("sourceUrl","https://example.org/verified"),("reviewed","true")};
            Assert.Equal(HttpStatusCode.Redirect,(await Submit("/admin/midias/video",fields)).StatusCode);
            var mediaId=await db.QuerySingleAsync<long>("SELECT id FROM restaurant_media WHERE restaurant_id=@id",new{id});
            var pending=await client.GetStringAsync("/establishment/"+id);
            Assert.DoesNotContain("data-video-embed",pending);
            Assert.Contains("Ilustra",pending);
            Assert.Equal(HttpStatusCode.Redirect,(await Submit("/admin/midias/decidir",("id",mediaId.ToString()),("restaurantId",id.ToString()),("decision","approved"))).StatusCode);
            var published=await client.GetStringAsync("/establishment/"+id);
            Assert.Contains("data-video-embed=\"https://www.youtube-nocookie.com/embed/abcdefghijk?rel=0\"",published);
            Assert.DoesNotContain("<iframe",published); // no third-party request before consent
            Assert.DoesNotContain("<img src=\"https://www.youtube.com",published);
            Assert.Equal(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE entity_type='restaurant_media' AND entity_id=@mediaId",new{mediaId}));
            client.DefaultRequestHeaders.Add("Test-Role",AdminRoles.Editor);
            Assert.Equal(HttpStatusCode.Forbidden,(await Submit("/admin/midias/decidir",("id",mediaId.ToString()),("restaurantId",id.ToString()),("decision","approved"))).StatusCode);
            client.DefaultRequestHeaders.Remove("Test-Role");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/admin/midias/video",new FormUrlEncodedContent([]))).StatusCode);
            var rejected=fields.Select(f=>f.Item1=="url"?("url","https://youtube.com.evil.invalid/watch?v=abcdefghijk"):f).ToArray();
            await Submit("/admin/midias/video",rejected);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurant_media WHERE restaurant_id=@id",new{id}));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='restaurant_media' AND entity_id IN (SELECT id FROM restaurant_media WHERE restaurant_id=@id)",new{id});
            await db.ExecuteAsync("DELETE FROM restaurant_media WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id});
        }
    }

    [Fact]
    public async Task FilterSelectionTakesNextSafeBatchAndPreservesPagination()
    {
        using var app=BulkAndVisitTests.Factory().WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Moderation:BatchLimit"]="2"})));
        using var scope=app.Services.CreateScope();var bulk=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();
        var marker="batch-cap-"+Guid.NewGuid().ToString("N");var ids=new List<long>();var batches=new List<string>();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            for(var i=0;i<3;i++)ids.Add(await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new(marker+i,marker+i,null,"Belém","Restaurante","Rua teste "+i,null,null,"https://example.org/source",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default));
            var filter=new ModerationFilter(Term:marker,PageSize:120);
            Assert.Equal(120,(await bulk.ListAsync(filter,1,default)).PageSize);
            var first=await bulk.PrepareAsync("fixture-admin","approve",null,[],true,filter,default);batches.Add(first.Id);
            Assert.Equal(2,first.Items.Count);
            var result=await bulk.ExecuteAsync(first.Id,"fixture-admin",default);
            Assert.Equal(2,result!.Results!.Count(r=>r.Outcome=="applied"));
            var next=await bulk.PrepareAsync("fixture-admin","approve",null,[],true,filter,default);batches.Add(next.Id);
            Assert.Single(next.Items);
            Assert.DoesNotContain(next.Items[0].Id,first.Items.Select(i=>i.Id));
        }
        finally
        {
            if(batches.Count>0)await db.ExecuteAsync("DELETE FROM moderation_batches WHERE id IN @batches",new{batches});
            if(ids.Count>0){await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id IN @ids",new{ids});await db.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids",new{ids});}
        }
    }

    [Theory]
    [InlineData("Açaí","acai")][InlineData("Pizzaria","pizza")][InlineData("Cafeteria","coffee")][InlineData("Bar","bar")][InlineData("Padaria","bread")][InlineData("Lanchonete","snack")][InlineData("Sorveteria","icecream")][InlineData("Peixes","fish")][InlineData(null,"plate")]
    public void CategoryArtMatchesType(string? category,string key)=>Assert.Equal(key,CategoryArtwork.For(category).Key);

    [Theory]
    [InlineData("javascript:alert(1)")][InlineData("https://youtube.com.evil.invalid/watch?v=abcdefghijk")][InlineData("https://www.youtube.com/@channel")][InlineData("https://www.instagram.com/profile/")][InlineData("https://user@youtu.be/abcdefghijk")]
    public void RejectsNonVideoAndUntrustedLinks(string url)=>Assert.Null(RestaurantVideo.From(url));
}
