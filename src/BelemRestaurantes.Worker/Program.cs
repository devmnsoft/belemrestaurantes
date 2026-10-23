using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Application;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseWindowsService(options => options.ServiceName = "BelemRestauranteCuiaDiscovery")
            .ConfigureServices((hostContext, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(hostContext.Configuration);
                services.AddScoped<BelemRestaurantes.Application.Abstractions.IAdminUserRepository,BelemRestaurantes.Infrastructure.MySql.MySqlAdminUserRepository>();
                services.AddHostedService<ConfigurationDiagnosticsService>();
                services.AddHostedService<MigrationHostedService>();
                services.AddHostedService<WorkerHealthService>();
                services.AddHostedService<LocalDevelopmentLifetime>();
                services.AddHostedService<EditorialReviewHostedService>();
                services.AddHostedService<DiscoveryHostedService>();
                services.AddHostedService<EmailHostedService>();
                services.AddHostedService<CrawlerHostedService>();
                services.AddHostedService<ResearchHostedService>();
                services.AddHostedService<CulinaryResearchHostedService>();
                services.AddHostedService<GeocodingHostedService>();
                services.AddHostedService<MaintenanceHostedService>();
            })
            .ConfigureLogging((context,logging) => {
                logging.AddConsole();
                var path=context.Configuration["Worker:LogDirectory"]??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MNSOFT","BelemRestaurantes","logs");
                logging.AddProvider(new WorkerFileLoggerProvider(Path.GetFullPath(path)));
            })
            .Build();
        if(args.Contains("--culinary-report",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();
            using var db=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default).GetAwaiter().GetResult();
            var report=Dapper.SqlMapper.Query(db,"SELECT id,kind,title,canonical_url,JSON_LENGTH(JSON_EXTRACT(body,'$.Ingredients')) ingredients FROM culinary_references WHERE state='published' ORDER BY id");
            var queue=Dapper.SqlMapper.Query(db,"SELECT state,last_status,COUNT(*) total FROM culinary_tasks GROUP BY state,last_status");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{report,queue}));return;
        }
        if(args.Contains("--collect-culinary",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default).GetAwaiter().GetResult();
            if(args.Contains("--refresh-culinary"))
            {
                using var db=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default).GetAwaiter().GetResult();
                Dapper.SqlMapper.Execute(db,"UPDATE culinary_tasks SET state='pending',next_run_at=UTC_TIMESTAMP(6) WHERE last_status IN ('no_structured_evidence','published') AND state='completed'");
            }
            var result=CulinaryResearchHostedService.RunAsync(scope.ServiceProvider,!args.Contains("--no-discovery"),default).GetAwaiter().GetResult();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
        }
        if(args.Contains("--geocode-pending",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default).GetAwaiter().GetResult();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(BelemRestaurantes.Worker.GeocodingHostedService.RunAsync(scope.ServiceProvider,default).GetAwaiter().GetResult()));return;
        }
        if(args.Contains("--verify-geocoding",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();
            var sp=scope.ServiceProvider;
            var summary=sp.GetRequiredService<BelemRestaurantes.Application.Abstractions.IGeocodingQueue>().SummaryAsync(default).GetAwaiter().GetResult();
            // Sprint 71 — o resumo da fila ganha os espelhos Overpass (mesmo helper e veredito do painel):
            // { queue: <resumo anterior>, mirrors: [<espelho configurado + última observação + status>] }.
            var mirrorRows=BelemRestaurantes.Application.Abstractions.MirrorFreshnessViewRow.Merge(
                sp.GetRequiredService<BelemRestaurantes.Infrastructure.Crawler.OverpassOptions>().Endpoints,
                sp.GetRequiredService<BelemRestaurantes.Application.Abstractions.IOverpassMirrorFreshnessStore>().GetLatestAsync(default).GetAwaiter().GetResult(),
                System.DateTimeOffset.UtcNow);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{queue=summary,mirrors=mirrorRows}));return;
        }
        if(args.Contains("--revert-geocode",StringComparer.Ordinal))
        {
            var idx=args.ToList().IndexOf("--revert-geocode");
            if(idx+1>=args.Length || !long.TryParse(args[idx+1],out var revertId) || revertId<=0)
                throw new ArgumentException("Informe o ID da ficha após --revert-geocode.");
            using var scope=host.Services.CreateScope();var sp=scope.ServiceProvider;
            var queue=sp.GetRequiredService<BelemRestaurantes.Application.Abstractions.IGeocodingQueue>();
            var result=queue.RevertAppliedCoordinatesAsync(revertId,"worker-cli",default).GetAwaiter().GetResult();
            if(result is not null)
            {
                sp.GetRequiredService<BelemRestaurantes.Application.Abstractions.IRestaurantAttributeHistoryRepository>().RecordHistoryAsync(new BelemRestaurantes.Domain.Entities.RestaurantAttributeHistory
                {
                    RestaurantId=revertId,
                    AttributeName="coordinate",
                    OldValue=$"{result.Latitude.ToString("0.0000000",System.Globalization.CultureInfo.InvariantCulture)};{result.Longitude.ToString("0.0000000",System.Globalization.CultureInfo.InvariantCulture)}",
                    NewValue="(removido por revisão administrativa)",
                    SourceType="admin_review",
                    SourceReference=result.Source,
                    ConfidenceScore=1.000m,
                    RequiresModeration=false,
                    IsApplied=true
                },default).GetAwaiter().GetResult();
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{RestaurantId=revertId,Reverted=result is not null,Latitude=result?.Latitude,Longitude=result?.Longitude,Source=result?.Source}));return;
        }
        if(args.Contains("--probe-drael",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();var sp=scope.ServiceProvider;
            var quota=sp.GetRequiredService<BelemRestaurantes.Application.Abstractions.IFreeTierQuota>();
            var options=sp.GetRequiredService<BelemRestaurantes.Infrastructure.Ai.DraelOptions>();
            if(!options.Ready)throw new InvalidOperationException("Drael não configurado.");
            if(!args.Contains("--search-only")&&quota.TryReserveAsync("cuia_drael",options.DailyRequestLimit,options.MonthlyRequestLimit,default).GetAwaiter().GetResult())
            {
                try{var response=sp.GetRequiredService<BelemRestaurantes.Infrastructure.Ai.DraelProvider>().CompleteAsync(new("Responda somente JSON {\"answer\":\"Olá, posso ajudar com a culinária do Pará.\",\"externalSuggestions\":[]}","Apresente-se brevemente.","probe_drael",128,0.1,true),default).GetAwaiter().GetResult();Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{stage="generation",response.IsValid,response.InputTokens,response.OutputTokens,characters=response.RawContent.Length}));}
                catch(Exception ex){Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{stage="generation",failureType=ex.GetType().Name}));}
            }
            var search=sp.GetRequiredService<BelemRestaurantes.Infrastructure.Ai.DraelWebSearch>().SearchAsync("restaurante Na Maré Belém cardápio Veja",default).GetAwaiter().GetResult();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{stage="search",search.Status,search.FailureCode,sources=search.Sources.Select(s=>new{s.Title,s.Url,s.SourceKind,s.RetrievalStatus})}));return;
        }
        var suspendContent=args.FirstOrDefault(a=>a.StartsWith("--suspend-content=",StringComparison.Ordinal));
        if(suspendContent is not null)
        {
            var ids=suspendContent["--suspend-content=".Length..].Split(',').Select(long.Parse).Distinct().ToArray();if(ids.Length>20)throw new ArgumentException("Limite de 20 referências.");
            using var scope=host.Services.CreateScope();var content=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IExternalContent>();
            foreach(var id in ids)Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{id,suspended=content.SetStateAsync(id,"suspended","system:content-quality",default).GetAwaiter().GetResult()}));return;
        }
        var metadataImport=Array.IndexOf(args,"--import-reference-metadata");
        if(metadataImport>=0 && metadataImport+1<args.Length)
        {
            var file=new FileInfo(args[metadataImport+1]);if(file.Length>131072)throw new ArgumentException("Metadados excedem 128 KiB.");
            var records=System.Text.Json.JsonSerializer.Deserialize<BelemRestaurantes.Application.Abstractions.ExternalContent[]>(File.ReadAllText(file.FullName))??[];
            if(records.Length>20)throw new ArgumentException("Importe até 20 referências por lote.");
            using var scope=host.Services.CreateScope();var content=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IExternalContent>();
            foreach(var record in records){record.ObservedAt=DateTime.UtcNow;var id=content.ObserveAsync(record,default).GetAwaiter().GetResult();Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{id,record.Title,record.Url}));}return;
        }
        var referenceFeed=args.FirstOrDefault(a=>a.StartsWith("--reference-feed=",StringComparison.Ordinal));
        if(referenceFeed is not null){ReferenceFeedCommand.RunAsync(host.Services,referenceFeed[17..],args.FirstOrDefault(a=>a.StartsWith("--city=",StringComparison.Ordinal))?[7..]??"Belém").GetAwaiter().GetResult();return;}
        if(args.Contains("--schedule-belem-open-data",StringComparer.Ordinal)){Sprint48Operations.ScheduleBelemAsync(host.Services).GetAwaiter().GetResult();return;}
        if(args.Contains("--verify-sprint48",StringComparer.Ordinal)){Sprint48Operations.PrintAsync(host.Services).GetAwaiter().GetResult();return;}
        var referenceImport=Array.IndexOf(args,"--import-place-reference");
        if(referenceImport>=0 && referenceImport+1<args.Length){Sprint48Operations.ImportReferenceAsync(host.Services,args[referenceImport+1]).GetAwaiter().GetResult();return;}
        var referenceReview=Array.IndexOf(args,"--approve-place-reference");
        if(referenceReview>=0 && referenceReview+1<args.Length){Sprint48Operations.ApproveReferenceAsync(host.Services,long.Parse(args[referenceReview+1])).GetAwaiter().GetResult();return;}
        var aiProbe=Array.IndexOf(args,"--probe-free-ai");
        if(aiProbe>=0 && aiProbe+1<args.Length){Sprint48Operations.ProbeFreeAiAsync(host.Services,args[aiProbe+1]).GetAwaiter().GetResult();return;}
        if (args.Contains("--verify-discovery", StringComparer.Ordinal))
        {
            DiscoveryEvidence.PrintAsync(host.Services).GetAwaiter().GetResult();
            return;
        }
        if(args.Contains("--seed-para-recipes",StringComparer.Ordinal))
        {EditorialHomologation.SeedAsync(host.Services,args).GetAwaiter().GetResult();return;}
        if(args.Contains("--configure-statewide-discovery",StringComparer.Ordinal))
        {EditorialHomologation.ConfigureCoverageAsync(host.Services,args).GetAwaiter().GetResult();return;}
        if(args.Contains("--queue-maintenance-checks",StringComparer.Ordinal))
        {EditorialHomologation.QueueChecksAsync(host.Services,args).GetAwaiter().GetResult();return;}
        if(args.Contains("--curate-hours",StringComparer.Ordinal))
        {EditorialHomologation.CurateHoursAsync(host.Services,args).GetAwaiter().GetResult();return;}
        if(args.Contains("--review-local-catalog",StringComparer.Ordinal)||args.Contains("--publish-reviewed-catalog",StringComparer.Ordinal))
        {EditorialHomologation.ReviewAsync(host.Services,args,args.Contains("--publish-reviewed-catalog",StringComparer.Ordinal)).GetAwaiter().GetResult();return;}
        if(args.Contains("--verify-operations",StringComparer.Ordinal))
        {OperationEvidence.PrintAsync(host.Services).GetAwaiter().GetResult();return;}
        if(args.Contains("--sweep-licensed-photos",StringComparer.Ordinal))
        {LicensedPhotoSweepCommand.RunAsync(host.Services).GetAwaiter().GetResult();return;}
        if(args.Contains("--issue-admin-recovery",StringComparer.Ordinal))
        {
            AdminRecoveryIssue.RunAsync(host.Services,args.Contains("--authorize-homologation",StringComparer.Ordinal)).GetAwaiter().GetResult();
            return;
        }
        if(args.Contains("--provision-homologation",StringComparer.Ordinal))
        {
            HomologationProvision.RunAsync(host.Services,args.Contains("--authorize-homologation",StringComparer.Ordinal),args.Contains("--reset-homologation",StringComparer.Ordinal)).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--retry-discovery-belem", StringComparer.Ordinal))
        {
            using var scope = host.Services.CreateScope();
            var count = scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IDiscoveryQueue>()
                .RetryDeadLettersAsync("Belém", default).GetAwaiter().GetResult();
            Console.WriteLine($"Belém: {count} jobs esgotados reagendados. Jobs em execução foram preservados.");
            return;
        }
        if (args.Contains("--probe-public-search",StringComparer.Ordinal))
        {
            using var scope=host.Services.CreateScope();
            var question=args.FirstOrDefault(a=>a.StartsWith("--query=",StringComparison.Ordinal))?[8..]??"Ilha do Combu";
            var result=scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Application.Abstractions.IWebSearch>().SearchAsync(question,default).GetAwaiter().GetResult();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
        }
        if (args.Contains("--import-open-data", StringComparer.Ordinal))
        {
            var city = args.FirstOrDefault(a => a.StartsWith("--city=", StringComparison.Ordinal))?[7..]
                ?? throw new ArgumentException("Informe --city=Município para importar a fonte aberta.");
            Environment.ExitCode = OpenDataImport.RunAsync(host.Services, city,
                args.Contains("--refresh-open-data", StringComparer.Ordinal),int.Parse(args.FirstOrDefault(a=>a.StartsWith("--max-records=",StringComparison.Ordinal))?[14..]??"2000",System.Globalization.CultureInfo.InvariantCulture)).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--import-food-registry", StringComparer.Ordinal))
        {
            var path=args.FirstOrDefault(a=>a.StartsWith("--file=",StringComparison.Ordinal))?[7..]
                ?? throw new ArgumentException("Informe --file=arquivo.ndjson coletado da fonte cadastral.");
            var limit=args.FirstOrDefault(a=>a.StartsWith("--max-new=",StringComparison.Ordinal))?[10..]??"5000";
            Environment.ExitCode=FoodRegistryImport.RunAsync(host.Services,path,int.Parse(limit,System.Globalization.CultureInfo.InvariantCulture)).GetAwaiter().GetResult();
            return;
        }
        host.Run();
    }
}

/// <summary>
/// Aplica as migrações embutidas antes do ciclo de descoberta. Fonte única de
/// schema: os scripts em Infrastructure/Migrations/Scripts. Sem banco
/// disponível o serviço para — descoberta sem schema é incidente, não degradação.
/// </summary>
public sealed class MigrationHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:ApplyMigrations", false))
        {
            logger.LogInformation("Aplicação de migrações desativada (Database:ApplyMigrations=false).");
            return;
        }

        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        var result = await runner.ApplyAsync(cancellationToken);

        logger.LogInformation(
            "Migrações verificadas: {Applied} aplicadas, {Current} já em vigor.",
            result.Applied, result.AlreadyCurrent.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
