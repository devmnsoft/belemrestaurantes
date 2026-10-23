using System.Net;
using System.Text;
using BelemRestaurantes.Infrastructure.Ai;
namespace BelemRestaurantes.UnitTests;
public sealed class DraelTests
{
    private sealed class Handler(string events):HttpMessageHandler
    {
        public string? Body;public Uri? Url;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Url=request.RequestUri;Body=await request.Content!.ReadAsStringAsync(ct);return new(HttpStatusCode.OK){Content=new StringContent(events,Encoding.UTF8,"text/event-stream")};}
    }
    [Fact]public async Task SynthesisDisablesNativeToolsAndDoesNotExposeReasoning()
    {
        var h=new Handler("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"private reasoning\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Resposta\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":12}}\n\ndata: [DONE]\n");
        var c=new DraelClient(new HttpClient(h),new(){Enabled=true,PlanConfirmed=true,ApiKey="test-only"});var reply=await c.SendAsync("instrução","tacacá",false,default);
        Assert.True(reply.Valid);Assert.Equal("Resposta",reply.Content);Assert.Equal(12,reply.OutputTokens);Assert.Contains("\"tools\":[]",h.Body);Assert.Equal("drael.sh",h.Url!.Host);Assert.DoesNotContain("test-only",h.Url.AbsoluteUri);
    }
    [Fact]public async Task SearchRequiresTraceOfActualSearchTool()
    {
        var c=new DraelClient(new HttpClient(new Handler("data: {\"choices\":[{\"delta\":{\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n")),new(){Enabled=true,PlanConfirmed=true,ApiKey="test-only"});
        Assert.False((await c.SendAsync("instrução","restaurantes",true,default)).Valid);
    }
    private sealed class Quota:BelemRestaurantes.Application.Abstractions.IFreeTierQuota
    {public Task<bool> TryReserveAsync(string service,int daily,int monthly,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class CapturingQuota:BelemRestaurantes.Application.Abstractions.IFreeTierQuota
    {public List<string> Services=new();public bool Grant=true;
     public Task<bool> TryReserveAsync(string service,int daily,int monthly,CancellationToken ct){Services.Add(service);return Task.FromResult(Grant);}}
    [Fact]public async Task NativeSearchRejectsUnrelatedTitlesAndPrivateUrls()
    {
        var body=System.Text.Json.JsonSerializer.Serialize(new{sources=new[]{new{title="Tacacá em Belém",url="https://example.org/tacaca"},new{title="Aplicativo de sobremesa",url="https://example.org/spam"},new{title="Tacacá em Belém",url="https://127.0.0.1/private"}}});
        var chunk=System.Text.Json.JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=body},finish_reason="stop"}},drael=new{used=new[]{"web_search"}}});
        var options=new DraelOptions{Enabled=true,PlanConfirmed=true,SearchEnabled=true,ApiKey="test-only"};
        var client=new DraelClient(new HttpClient(new Handler("data: "+chunk+"\n\ndata: [DONE]\n")),options);
        var result=await new DraelWebSearch(client,options,new Quota()).SearchAsync("tacacá Belém",default);
        Assert.Equal("ok",result.Status);Assert.Single(result.Sources);Assert.Equal("ai_discovery",result.Sources[0].SourceKind);Assert.Equal("unverified",result.Sources[0].RetrievalStatus);Assert.DoesNotContain("sobremesa",result.Sources[0].Snippet);
    }
    [Fact]public async Task PaidPlanNeedsExplicitConfirmationBeforeAnyNetworkCall()
    {
        var options=new DraelOptions{Enabled=true,SearchEnabled=true,ApiKey="test-only"};var handler=new Handler("");
        var result=await new DraelWebSearch(new DraelClient(new HttpClient(handler),options),options,new Quota()).SearchAsync("tacacá",default);
        Assert.Equal("not_configured",result.Status);Assert.Null(handler.Url);
    }
    [Fact]public async Task SearchUsesItsOwnQuotaSoGenerationIsNotStarved()
    {
        // A descoberta por busca tem cota própria (drael_search): o teto diário de geração (cuia_drael)
        // não pode ser consumido pelas pesquisas antes de o modelo responder.
        var quota=new CapturingQuota{Grant=false};var handler=new Handler("");
        var options=new DraelOptions{Enabled=true,PlanConfirmed=true,SearchEnabled=true,ApiKey="test-only"};
        var result=await new DraelWebSearch(new DraelClient(new HttpClient(handler),options),options,quota).SearchAsync("tacacá Belém",default);
        Assert.Equal("quota_exhausted",result.Status);
        Assert.Equal(new[]{"drael_search"},quota.Services);
        Assert.Null(handler.Url);
    }
    [Fact]public async Task SynthesisAcceptsOneEnclosingJsonCodeFence()
    {
        var chunk=System.Text.Json.JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content="```json\n{\"answer\":\"Resposta\"}\n```"},finish_reason="stop"}}});
        var options=new DraelOptions{Enabled=true,PlanConfirmed=true,ApiKey="test-only"};var provider=new DraelProvider(new DraelClient(new HttpClient(new Handler("data: "+chunk+"\n\ndata: [DONE]\n")),options),options);
        var response=await provider.CompleteAsync(new("instrução","tacacá","test",128,0.1,true),default);Assert.Equal("{\"answer\":\"Resposta\"}",response.RawContent);
    }
    private sealed class DelayedStream(Stream inner,int delayMs):Stream
    {
        private bool waited;
        public override bool CanRead=>true;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>false;
        public override long Length=>inner.Length;
        public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override void Flush()=>inner.Flush();
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken){if(!waited){waited=true;await Task.Delay(delayMs,cancellationToken);}return await inner.ReadAsync(buffer,cancellationToken);}
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class DelayedHandler(int delayMs):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult<HttpResponseMessage>(new(HttpStatusCode.OK){Content=new StreamContent(new DelayedStream(new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"{}\"},\"finish_reason\":\"stop\"}],\"drael\":{\"used\":[\"web_search\"]}}\n\ndata: [DONE]\n")),delayMs))});
    }
    [Fact]public async Task SearchBudgetCoversMeasuredLatencyAndDegradesToTimeoutWhenExceeded()
    {
        // Medição ao vivo (Sprint 74): a busca nativa levou 12,9–17,2 s nesta conta — o orçamento fixo de 15 s
        // cortava buscas válidas no meio do stream e virava "unavailable" sem nenhum motivo visível no log.
        Assert.Equal(20,new DraelOptions().SearchTimeoutSeconds);
        var options=new DraelOptions{Enabled=true,PlanConfirmed=true,SearchEnabled=true,ApiKey="test-only",SearchTimeoutSeconds=1};
        var result=await new DraelWebSearch(new DraelClient(new HttpClient(new DelayedHandler(2000)),options),options,new Quota()).SearchAsync("tacacá Belém",default);
        Assert.Equal("unavailable",result.Status);Assert.Equal("timeout",result.FailureCode);Assert.Empty(result.Sources);
    }
}
