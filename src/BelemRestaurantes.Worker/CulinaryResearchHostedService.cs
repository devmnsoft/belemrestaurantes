using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

public sealed class CulinaryResearchHostedService(IServiceScopeFactory scopes,IConfiguration configuration,ILogger<CulinaryResearchHostedService> logger):BackgroundService
{
    public static async Task<CulinaryCollection> RunAsync(IServiceProvider services,bool discover,CancellationToken ct)
    {
        var research=services.GetRequiredService<ICulinaryResearch>();
        await research.EnqueueAsync(["https://gourmetadois.com/receita/hamburguer-de-camarao-162.html","https://www.saborintenso.com/f17/hamburguer-camarao-43240/"],"hambúrguer de camarão",ct);
        await research.EnqueueAsync(["https://receitas.globo.com/regionais/tv-liberal/e-do-para/tacaca.ghtml"],"tacacá",ct);
        await research.EnqueueAsync(["https://receitas.globo.com/regionais/tv-liberal/e-do-para/peixe-e-do-para.ghtml"],"peixe",ct);
        await research.EnqueueAsync([BelemRestaurantes.Infrastructure.Crawler.RemansoMenuSource.Url],"pratos paraenses",ct);
        if(discover)
        {
            await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(ct);
            // Reservation before calling a provider. Shared across instances and surviving restarts.
            foreach(var topic in new[]{"hambúrguer de camarão","tacacá receita paraense"})
            {
                var reserved=await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO culinary_search_runs(topic,run_day,status) VALUES(@topic,UTC_DATE(),'running')",new{topic},cancellationToken:ct));
                if(reserved==0)continue;
                var result=await services.GetRequiredService<IWebSearch>().SearchAsync(topic+" Belém restaurante receita vídeo",ct);
                var eligible=SearchRanking.Merge(topic,result.Sources);
                await research.EnqueueAsync(eligible.Select(s=>s.Url),topic,ct);
                await db.ExecuteAsync(new CommandDefinition("UPDATE culinary_search_runs SET status=@Status,source_count=@count WHERE topic=@topic AND run_day=UTC_DATE()",new{topic,result.Status,count=eligible.Count},cancellationToken:ct));
            }
        }
        return await research.CollectAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(configuration.GetValue<bool>("Worker:ResearchEnabled")&&configuration.GetValue<bool>("Crawler:Enabled"))
                {
                    using var scope=scopes.CreateScope();
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);deadline.CancelAfter(TimeSpan.FromSeconds(110));
                    var result=await RunAsync(scope.ServiceProvider,true,deadline.Token);
                    logger.LogInformation("Coleta culinária: {Discovered} tarefas, {Published} publicações/atualizações, {Deferred} adiadas.",result.Discovered,result.Published,result.Deferred);
                }
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning("Ciclo culinário: {Failure}.",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(60),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
