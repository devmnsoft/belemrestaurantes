using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace BelemRestaurantes.UnitTests;
public sealed class CrawlerRobotsTests
{
    private sealed class Http(SafeDownloadResult result):ISafeHttpClient
    {
        public int Calls {get;private set;}
        public Task<SafeDownloadResult> DownloadAsync(string url,string? etag=null,string? lastModified=null,int timeoutSeconds=15,CancellationToken ct=default)
        {Calls++;return Task.FromResult(result);}
    }
    [Theory]
    [InlineData(0)][InlineData(403)][InlineData(429)][InlineData(503)]
    public async Task UnknownOrDeniedPermissionDefersCrawlingAndCachesTheFailure(int status)
    {
        var http=new Http(new(false,status,null,null,null,null,"Unavailable"));
        var service=new RobotsTxtService(http,NullLogger<RobotsTxtService>.Instance);
        var url="https://"+Guid.NewGuid().ToString("N")+".example.org/menu";
        Assert.False((await service.CheckAccessAsync(url)).IsAllowed);
        Assert.False((await service.CheckAccessAsync(url)).IsAllowed);
        Assert.Equal(1,http.Calls);
    }
    [Theory]
    [InlineData(404)][InlineData(410)][InlineData(200)]
    public async Task ExplicitlyAbsentOrValidEmptyRobotsAllowsConservativeAccess(int status)
    {
        var http=new Http(new(status==200,status,"","text/plain",null,null,null));
        var service=new RobotsTxtService(http,NullLogger<RobotsTxtService>.Instance);
        var result=await service.CheckAccessAsync("https://"+Guid.NewGuid().ToString("N")+".example.org/menu");
        Assert.True(result.IsAllowed);Assert.Equal(5,result.CrawlDelaySeconds);
    }
    [Fact]
    public async Task PublishedRobotsRestrictionRemainsEffective()
    {
        var http=new Http(new(true,200,"User-agent: *\nDisallow: /private\nCrawl-delay: 12","text/plain",null,null,null));
        var service=new RobotsTxtService(http,NullLogger<RobotsTxtService>.Instance);
        var host="https://"+Guid.NewGuid().ToString("N")+".example.org";
        Assert.False((await service.CheckAccessAsync(host+"/private/menu")).IsAllowed);
        var allowed=await service.CheckAccessAsync(host+"/menu");Assert.True(allowed.IsAllowed);Assert.Equal(12,allowed.CrawlDelaySeconds);
    }
}
