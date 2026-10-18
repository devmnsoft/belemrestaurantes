using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Crawler;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class CrawlerQueueTests
{
    [Fact]
    public async Task PausedSourceKeepsItsPendingTaskUntilAnOperatorReactivatesIt()
    {
        using var app=BulkAndVisitTests.Factory();using var scope=app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var sources=scope.ServiceProvider.GetRequiredService<ICrawlerSourceRepository>();
        var queue=scope.ServiceProvider.GetRequiredService<ICrawlerTaskQueue>();
        var marker=Guid.NewGuid().ToString("N");
        var id=await sources.CreateSourceAsync(new CrawlerSource{Name="Queue fixture "+marker,BaseUrl="https://example.org/"+marker,SourceType="html",TargetCity="Belém",Status="paused",CrawlDelaySeconds=5},default);
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            await queue.EnqueueTaskAsync(new CrawlerTask{SourceId=id,Url="https://example.org/"+marker,City="Belém"},default);
            // Run only against the disposable CI database, alongside the concurrency suite.
            Assert.DoesNotContain(await queue.ClaimDueTasksAsync(100,"fixture-paused",TimeSpan.FromMinutes(1),default),t=>t.SourceId==id);
            Assert.Equal("pending",await db.ExecuteScalarAsync<string>("SELECT state FROM crawler_tasks WHERE source_id=@id",new{id}));
            await sources.UpdateStatusAsync(id,"active",default);
            Assert.Contains(await queue.ClaimDueTasksAsync(100,"fixture-reactivated",TimeSpan.FromMinutes(1),default),t=>t.SourceId==id);
        }
        finally{await db.ExecuteAsync("DELETE FROM crawler_sources WHERE id=@id",new{id});}
    }
}
