using System.Net;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Formatting;
using BelemRestaurantes.Infrastructure.Ai;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint50SearchTests
{
    [Fact] public async Task KeylessSearchHasFixedEndpointAttributionCacheAndQuota()
    {
        var handler=new Handler("""{"query":{"search":[{"pageid":42,"title":"Ilha do Combu","snippet":"Uma <span class='searchmatch'>ilha</span> do Pará."}]}}""");
        var quota=new Quota();var provider=new PublicKnowledgeWebSearch(new HttpClient(handler),new(){Enabled=true},quota,new());
        var first=await provider.SearchAsync("Ilha do Combu",default);Assert.Equal("public_sources",first.Status);var source=Assert.Single(first.Sources);Assert.Contains("CC BY-SA",source.Title);Assert.DoesNotContain("<span",source.Snippet);Assert.Equal("https://pt.wikipedia.org/?curid=42",source.Url);
        var second=await provider.SearchAsync("Ilha do Combu",default);Assert.Equal("public_sources_cached",second.Status);Assert.Equal(first.Sources[0].RetrievedAt,second.Sources[0].RetrievedAt);Assert.Equal(1,handler.Calls);Assert.Equal(1,quota.Calls);
        Assert.Equal("pt.wikipedia.org",handler.Uri!.Host);Assert.Equal("/w/api.php",handler.Uri.AbsolutePath);
    }
    [Fact] public async Task DisabledAndExhaustedSourcesNeverCallNetwork()
    {
        var handler=new Handler("{}");var http=new HttpClient(handler);Assert.Equal("not_configured",(await new PublicKnowledgeWebSearch(http,new(),new Quota(),new()).SearchAsync("Combu",default)).Status);
        Assert.Equal("quota_exhausted",(await new PublicKnowledgeWebSearch(http,new(){Enabled=true},new Quota{Allowed=false},new()).SearchAsync("Combu",default)).Status);Assert.Equal(0,handler.Calls);
    }
    [Theory][InlineData(302,"unavailable")][InlineData(429,"quota_exhausted")][InlineData(503,"unavailable")]
    public async Task FailureStatusesAreNotEmptyResults(int status,string expected)
    {var provider=new PublicKnowledgeWebSearch(new HttpClient(new Handler("",(HttpStatusCode)status)),new(){Enabled=true},new Quota(),new());Assert.Equal(expected,(await provider.SearchAsync("Combu",default)).Status);}
    [Fact] public void MarkdownNeverCreatesAnEmptyNumberedItem()
    {var html=CuiaMarkdown.ToHtml("### Ideias\n1.\n**Continuar nesta aba**\n\n1. Primeiro\n2. Segundo\n<script>alert(1)</script>");Assert.DoesNotContain("<li></li>",html);Assert.DoesNotContain("<p>1.</p>",html);Assert.Contains("<ol><li>Primeiro</li><li>Segundo</li></ol>",html);Assert.DoesNotContain("<script>",html);}
    [Fact] public void SearchStatesExplainDifferentActions()=>Assert.NotEqual(CuiaSearchPresentation.Message("not_configured"),CuiaSearchPresentation.Message("empty"));
    private sealed class Quota:IFreeTierQuota{public int Calls;public bool Allowed=true;public Task<bool> TryReserveAsync(string name,int day,int month,CancellationToken ct){Calls++;return Task.FromResult(Allowed);}}
    private sealed class Handler(string body,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {public int Calls;public Uri? Uri;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Uri=request.RequestUri;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")});}}
}
