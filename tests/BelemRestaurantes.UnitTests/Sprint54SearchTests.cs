using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Ai;
namespace BelemRestaurantes.UnitTests;
public sealed class Sprint54SearchTests
{
    [Fact]public void NaturalQuestionKeepsPlaceAndDropsFiller(){Assert.Equal(new[]{"terra","meio"},SearchLanguage.Terms("O que era a Terra do Meio?"));Assert.DoesNotContain("era",PublicKnowledgeWebSearch.BuildQuery("O que era a Terra do Meio?"));}
    [Theory][InlineData("Quero conhecer Marituba","Marituba")][InlineData("passeio em Benevides","Benevides")][InlineData("Sao Felix do Xingu","São Félix do Xingu")]
    public void MunicipalityDetectionCoversTheOfficialCatalog(string query,string city)=>Assert.Equal(city,SearchLanguage.Municipality(query));
    [Fact]public void PartialCityNamesDoNotMatch()=>Assert.Null(SearchLanguage.Municipality("maritubana"));
    private sealed class Quota:IFreeTierQuota{public Task<bool> TryReserveAsync(string s,int d,int m,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class Robots(bool allowed,int delay=0):IRobotsTxtService{public Task<(bool IsAllowed,int CrawlDelaySeconds)> CheckAccessAsync(string url,string userAgent="CuiaCrawler",CancellationToken ct=default)=>Task.FromResult((allowed,delay));}
    private sealed class Http:ISafeHttpClient
    {
        public int Calls;
        public Task<SafeDownloadResult> DownloadAsync(string url,string? etag=null,string? lastModified=null,int timeoutSeconds=15,CancellationToken ct=default)
        {
            Interlocked.Increment(ref Calls);Assert.DoesNotContain("era",new Uri(url).Query);Assert.Contains("%22terra%20do%20meio%22",new Uri(url).Query);var origin=new Uri(url).GetLeftPart(UriPartial.Authority);
            var json=System.Text.Json.JsonSerializer.Serialize(new[]{new{title="Terra do Meio e culinária",url=origin+"/materia"},new{title="Cross-site",url="https://other.example.org/source"},new{title="Projeto de reflorestamento",url=origin+"/unrelated"}});
            return Task.FromResult(new SafeDownloadResult(true,200,json,"application/json",null,null,null));
        }
    }
    [Fact]public async Task OfficialIndexesRequireSameHostAndRespectRobots()
    {
        var http=new Http();var search=new PublicSiteSearch(http,new Robots(true,5),new Quota(),new(){Enabled=true});var result=await search.SearchAsync("o que era a Terra do Meio",default);Assert.Equal(2,result.Sources.Count);Assert.Equal(2,http.Calls);Assert.All(result.Sources,s=>Assert.Equal("public_index",s.SourceKind));
        http=new();Assert.Empty((await new PublicSiteSearch(http,new Robots(false),new Quota(),new(){Enabled=true}).SearchAsync("Terra do Meio",default)).Sources);Assert.Equal(0,http.Calls);
    }
}
