using System.Net;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Ai;
namespace BelemRestaurantes.UnitTests;
// Sprint 75: a pesquisa contínua do Worker consumia o pool inteiro de busca (20/20 drael, 30/30 sites)
// nos primeiros minutos após a virada do dia — 152 jobs/dia, 132 deles marcados quota_exhausted na sequência —
// deixando a Cuia interativa sem cota até a próxima virada. Cada braço agora reserva o serviço configurado em
// suas opções; o Worker aponta os nomes para pools "worker_*" isolados (appsettings versionado).
public sealed class Sprint75PoolIsolationTests
{
    private sealed class Quota:IFreeTierQuota
    {
        public List<(string Service,int Daily,int Monthly)> Reservations=new();public bool Grant=true;
        public Task<bool> TryReserveAsync(string service,int daily,int monthly,CancellationToken ct){Reservations.Add((service,daily,monthly));return Task.FromResult(Grant);}
    }
    private sealed class NeverHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult<HttpResponseMessage>(new(HttpStatusCode.OK){Content=new StringContent("",Encoding.UTF8,"text/event-stream")});
    }
    [Fact]public void DefaultsKeepTheInteractivePoolNames()
    {
        var drael=new DraelOptions();
        Assert.Equal("drael_search",drael.SearchQuotaService);Assert.Equal(20,drael.SearchDailyRequestLimit);Assert.Equal(200,drael.SearchMonthlyRequestLimit);
        Assert.Equal("public_site_search",new PublicSiteSearchOptions().QuotaService);
        Assert.Equal("wikipedia_search",new PublicSourcesOptions().QuotaService);
    }
    [Fact]public async Task DraelSearchReservesTheConfiguredPoolWithItsOwnLimits()
    {
        var quota=new Quota{Grant=false};
        var options=new DraelOptions{Enabled=true,PlanConfirmed=true,SearchEnabled=true,ApiKey="test-only",SearchQuotaService="worker_drael_search",SearchDailyRequestLimit=10,SearchMonthlyRequestLimit=300};
        var result=await new DraelWebSearch(new DraelClient(new HttpClient(new NeverHandler()),options),options,quota).SearchAsync("tacacá Belém",default);
        Assert.Equal("quota_exhausted",result.Status);
        Assert.Single(quota.Reservations);
        Assert.Equal("worker_drael_search",quota.Reservations[0].Service);
        Assert.Equal(10,quota.Reservations[0].Daily);
        Assert.Equal(300,quota.Reservations[0].Monthly);
    }
    [Fact]public async Task PublicSiteIndexReservesTheConfiguredPool()
    {
        var quota=new Quota{Grant=false};
        var search=new PublicSiteSearch(new Http(),new Robots(),quota,new(){Enabled=true,QuotaService="worker_public_site_search",DailyRequestLimit=30});
        _=await search.SearchAsync("o que era a Terra do Meio",default);
        Assert.NotEmpty(quota.Reservations);
        Assert.All(quota.Reservations,r=>{Assert.Equal("worker_public_site_search",r.Service);Assert.Equal(30,r.Daily);});
    }
    [Fact]public async Task WikipediaReservesTheConfiguredPool()
    {
        var quota=new Quota{Grant=false};
        var provider=new PublicKnowledgeWebSearch(new HttpClient(new NeverHandler()),new(){Enabled=true,QuotaService="worker_wikipedia_search"},quota,new());
        Assert.Equal("quota_exhausted",(await provider.SearchAsync("Ilha do Combu",default)).Status);
        Assert.Single(quota.Reservations);
        Assert.Equal("worker_wikipedia_search",quota.Reservations[0].Service);
    }
    [Fact]public void WorkerAppsettingsRouteResearchToIsolatedPools()
    {
        var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","src","BelemRestaurantes.Worker","appsettings.json"));
        using var doc=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root=doc.RootElement;
        Assert.Equal("worker_drael_search",root.GetProperty("Ai").GetProperty("Drael").GetProperty("SearchQuotaService").GetString());
        Assert.True(root.GetProperty("Ai").GetProperty("Drael").GetProperty("SearchDailyRequestLimit").GetInt32()<=20,"o teto de pesquisa do Worker não pode exceder o pool interativo");
        Assert.Equal("worker_public_site_search",root.GetProperty("WebSearch").GetProperty("PublicSites").GetProperty("QuotaService").GetString());
    }
    private sealed class Robots:IRobotsTxtService{public Task<(bool IsAllowed,int CrawlDelaySeconds)> CheckAccessAsync(string url,string userAgent="CuiaCrawler",CancellationToken ct=default)=>Task.FromResult((true,0));}
    private sealed class Http:ISafeHttpClient{public Task<SafeDownloadResult> DownloadAsync(string url,string? etag=null,string? lastModified=null,int timeoutSeconds=15,CancellationToken ct=default)=>Task.FromResult(new SafeDownloadResult(true,200,"[]","application/json",null,null,null));}
}
