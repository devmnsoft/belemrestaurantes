using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Ai;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;
// Public city/category jobs only: no user conversations or personal data enter this queue.
public sealed class ResearchHostedService(IServiceScopeFactory scopes,IConfiguration config,ILogger<ResearchHostedService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(config.GetValue<bool>("Worker:ResearchEnabled") && config.GetValue<bool>("Crawler:Enabled"))
                {
                    using var scope=scopes.CreateScope();var services=scope.ServiceProvider;
                    // Encyclopedia-only fallback cannot discover operational restaurant records.
                    var tavily=services.GetRequiredService<TavilyOptions>();var brave=services.GetRequiredService<BraveSearchOptions>();
                    var ready=tavily.Enabled&&tavily.FreeTierConfirmed&&!string.IsNullOrWhiteSpace(tavily.ApiKey)||brave.Enabled&&brave.FreeTierConfirmed&&!string.IsNullOrWhiteSpace(brave.ApiKey);
                    ready=ready||services.GetRequiredService<PublicSiteSearchOptions>().Enabled||(services.GetRequiredService<DraelOptions>().Ready&&services.GetRequiredService<DraelOptions>().SearchEnabled);
                    if(!ready)logger.LogInformation("Pesquisa contínua aguardando provedor geral gratuito configurado; descoberta e crawler existentes continuam.");
                    else
                    {
                        var jobs=services.GetRequiredService<IResearchJobs>();await jobs.EnqueueAsync(stoppingToken);
                        for(var n=0;n<Math.Clamp(config.GetValue("Worker:ResearchBatchSize",2),1,5);n++)
                        {
                            var job=await jobs.ClaimAsync(stoppingToken);if(job is null)break;
                            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);deadline.CancelAfter(TimeSpan.FromSeconds(60));
                            try
                            {
                                var result=await services.GetRequiredService<IWebSearch>().SearchAsync(job.Query,deadline.Token);
                                result=await services.GetRequiredService<IResearchEvidence>().EnrichAsync(result,job.City,deadline.Token);
                                if(BelemRestaurantes.Application.Services.CulinaryQuery.IsCulinary(job.Query))
                                    await services.GetRequiredService<ICulinaryResearch>().EnqueueAsync(result.Sources.Select(s=>s.Url),job.Query,deadline.Token);
                                var content=services.GetRequiredService<IExternalContent>();
                                foreach(var item in result.Sources.Where(s=>(s.RetrievalStatus=="retrieved"||s.SourceKind=="social_index")&&s.SourceKind!="encyclopedia").Take(8))
                                    await content.ObserveAsync(new ExternalContent{Title=item.Title,Url=item.Url,City=item.City??job.City,Summary=item.Snippet,ObservedAt=item.RetrievedAt.UtcDateTime,PublishedAt=item.PublishedAt?.UtcDateTime,FactsJson=System.Text.Json.JsonSerializer.Serialize(new ExternalContentFacts(item.PlaceName,item.Address,item.MenuItems,item.OpeningHours))},deadline.Token);
                                var sources=services.GetRequiredService<ICrawlerSourceRepository>();var queue=services.GetRequiredService<ICrawlerTaskQueue>();var queued=0;
                                foreach(var item in result.Sources.Where(s=>s.SourceKind=="structured_site"&&s.RetrievalStatus=="retrieved"&&!string.IsNullOrWhiteSpace(s.Address)).Take(3))
                                {
                                    var source=await sources.GetByBaseUrlAsync(item.Url,deadline.Token);
                                    if(source is not null && source.Status!="active")continue;
                                    var id=source?.Id??await sources.CreateSourceAsync(new CrawlerSource{Name="Pesquisa Cuia · "+item.Title[..Math.Min(120,item.Title.Length)],BaseUrl=item.Url,SourceType="structured_data",TargetCity=job.City,Status="active"},deadline.Token);
                                    if(await queue.EnqueueTaskAsync(new CrawlerTask{SourceId=id,Url=item.Url,City=job.City},deadline.Token))queued++;
                                }
                                await jobs.CompleteAsync(job.Id,result.Status,result.Sources.Count,queued,stoppingToken);
                                logger.LogInformation("Pesquisa contínua {JobId}: {Status}, {Sources} fontes, {Queued} páginas estruturadas enfileiradas.",job.Id,result.Status,result.Sources.Count,queued);
                            }
                            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){throw;}
                            catch(Exception ex){await jobs.CompleteAsync(job.Id,ex is OperationCanceledException?"timeout":"unavailable",0,0,stoppingToken);logger.LogWarning("Pesquisa contínua {JobId}: {Failure}.",job.Id,ex.GetType().Name);}
                        }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Worker:ResearchPollSeconds",60),60,3600)),stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError("Ciclo de pesquisa contínua falhou: {Failure}.",ex.GetType().Name);await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}
        }
    }
}
