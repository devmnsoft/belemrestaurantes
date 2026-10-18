using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.OpenData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>Importa páginas locais minimizadas da fonte pública, com trilha e triagem humana.</summary>
internal static class FoodRegistryImport
{
    public static async Task<int> RunAsync(IServiceProvider services, string path, int maxNew)
    {
        if(maxNew is <1 or >20000) throw new ArgumentOutOfRangeException(nameof(maxNew));
        var file=new FileInfo(Path.GetFullPath(path));
        if(!file.Exists || file.Length>200_000_000) throw new ArgumentException("Arquivo cadastral ausente ou acima de 200 MB.");
        using var scope=services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var telemetry=scope.ServiceProvider.GetRequiredService<ICycleTelemetryRepository>();
        var clock=scope.ServiceProvider.GetRequiredService<IClock>();
        var cycle=await telemetry.StartCycleAsync("registry-import:"+Environment.MachineName,clock.UtcNow,default);
        var total=DiscoveryCycleMetrics.Empty with { Source="minha_receita" };
        var byCity=new Dictionary<string,DiscoveryCycleMetrics>();
        var watch=Stopwatch.StartNew();
        var pages=0;var read=0;var filtered=0;var failed=false;
        try
        {
            foreach(var line in File.ReadLines(file.FullName))
            {
                if(string.IsNullOrWhiteSpace(line))continue;
                if(line.Length>4_000_000)throw new InvalidOperationException("Página acima do limite de 4 MB.");
                var batch=FoodRegistryParser.ParsePage(line);
                pages++;read+=batch.ReadCount;filtered+=batch.ReadCount-batch.Places.Count;
                foreach(var city in batch.Places.GroupBy(p=>p.ResolvedCity))
                {
                    var cityPlaces=city.ToArray();
                    var offset=0;
                    while(offset<cityPlaces.Length && total.RestaurantsCreated<maxNew)
                    {
                    // Um lote nunca contém mais itens que o saldo de novas inserções autorizado.
                    var length=Math.Min(cityPlaces.Length-offset,maxNew-total.RestaurantsCreated);
                    var adapter=new LocalRegistryProvider(cityPlaces[offset..(offset+length)],batch.FetchedAt);
                    var orchestrator=new DiscoveryOrchestrator(adapter,
                        scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>(),
                        scope.ServiceProvider.GetRequiredService<IModerationRepository>(),
                        scope.ServiceProvider.GetRequiredService<IAiRouter>(),clock,
                        scope.ServiceProvider.GetRequiredService<ILogger<DiscoveryOrchestrator>>());
                    var metrics=await orchestrator.RunTargetAsync(new(0,0,city.Key,"alimentação",1),default);
                    total=Add(total,metrics);
                    byCity[city.Key]=Add(byCity.GetValueOrDefault(city.Key)??DiscoveryCycleMetrics.Empty,metrics);
                    offset+=length;
                    }
                    if(total.RestaurantsCreated>=maxNew)break;
                }
                Console.WriteLine($"Cadastro PA: páginas={pages}, lidos={read}, filtrados={filtered}, novos={total.RestaurantsCreated}, atualizados={total.RestaurantsUpdated}, falhas={total.JobsFailed}.");
                if(total.RestaurantsCreated>=maxNew)break;
            }
        }
        catch(Exception ex)
        {
            failed=true;
            Console.Error.WriteLine($"Importação interrompida ({ex.GetType().Name}); dados já confirmados foram preservados. Verifique o arquivo e retome.");
        }
        finally
        {
            foreach(var pair in byCity)
                await telemetry.RecordTargetAsync(cycle,new(0,0,pair.Key,"alimentação",1),pair.Value,default);
            await telemetry.CompleteCycleAsync(cycle,0,total.PlacesDiscovered,total.RestaurantsCreated,total.RestaurantsUpdated,
                total.JobsFailed+(failed?1:0),total.PlacesSkipped,watch.Elapsed,default);
        }
        using var stream=file.OpenRead();
        var hash=Convert.ToHexString(SHA256.HashData(stream));
        await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteAsync("discovery.registry_import","discovery_cycle",cycle,
            JsonSerializer.Serialize(new{source="minha_receita",sha256=hash,pages,read,filtered,total.RestaurantsCreated,total.RestaurantsUpdated,total.JobsFailed,failed}),default);
        Console.WriteLine($"Importação cadastral concluída: ciclo={cycle}, municípios={byCity.Count}, novos={total.RestaurantsCreated}, falhas={total.JobsFailed+(failed?1:0)}. Todos os novos aguardam moderação; sem geocodificação inventada.");
        return failed || total.JobsFailed>0?1:0;
    }

    private static DiscoveryCycleMetrics Add(DiscoveryCycleMetrics a,DiscoveryCycleMetrics b)=>new(
        0,a.PlacesDiscovered+b.PlacesDiscovered,a.RestaurantsCreated+b.RestaurantsCreated,
        a.RestaurantsUpdated+b.RestaurantsUpdated,a.JobsFailed+b.JobsFailed,a.PlacesSkipped+b.PlacesSkipped,TimeSpan.Zero){Source="minha_receita"};

    private sealed class LocalRegistryProvider(IReadOnlyList<DiscoveredPlace> places,DateTimeOffset fetchedAt):IPlaceDiscoveryProvider
    {
        public string Name=>"minha_receita";
        public bool IsConfigured=>true;
        public Task<PlaceSearchResult> SearchAsync(string query,string? city,CancellationToken ct)=>Task.FromResult(new PlaceSearchResult(places,null,fetchedAt));
        public Task<DiscoveredPlace?> GetDetailsAsync(string id,CancellationToken ct)=>Task.FromResult<DiscoveredPlace?>(null);
    }
}
