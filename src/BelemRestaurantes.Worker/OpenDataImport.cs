using System.Diagnostics;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.OpenData;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Worker;

/// <summary>Importação pontual por município, usando a mesma validação/deduplicação do Worker.</summary>
internal static class OpenDataImport
{
    public static async Task<int> RunAsync(IServiceProvider services, string city, bool refresh,int limit=2000)
    {
        _ = OpenStreetMapDiscoveryClient.BuildQuery(city); // recusa município não oficial antes de qualquer escrita/chamada
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IPlaceDiscoveryProvider>();
        if (provider is not OpenStreetMapDiscoveryClient openData)
            throw new InvalidOperationException("A importação exige Discovery:Provider=OpenStreetMap.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = timeout.Token;
        await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(ct);
        if (refresh) await openData.RefreshAsync(city, ct);
        var telemetry = scope.ServiceProvider.GetRequiredService<ICycleTelemetryRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var cycle = await telemetry.StartCycleAsync($"manual-open-data:{Environment.MachineName}", clock.UtcNow, ct);
        var stopwatch = Stopwatch.StartNew();
        var work = new DiscoveryWorkItem(0, 0, city, "regional", 1);
        var orchestrator=new DiscoveryOrchestrator(new LimitedImport(provider,Math.Clamp(limit,1,2000)),
            scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>(),scope.ServiceProvider.GetRequiredService<IModerationRepository>(),
            scope.ServiceProvider.GetRequiredService<IAiRouter>(),clock,scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DiscoveryOrchestrator>>());
        var metrics=await orchestrator.RunTargetAsync(work,ct);
        // A importação não consome jobs da fila. O ciclo recebe os resultados reais, inclusive falhas parciais.
        await telemetry.RecordTargetAsync(cycle, work, metrics, ct);
        await telemetry.CompleteCycleAsync(cycle, 0, metrics.PlacesDiscovered, metrics.RestaurantsCreated,
            metrics.RestaurantsUpdated, metrics.JobsFailed, metrics.PlacesSkipped, stopwatch.Elapsed, ct);
        Console.WriteLine($"Importação {city}: fonte={provider.Name}, {metrics}. Novos cadastros aguardam moderação.");
        return metrics.JobsFailed == 0 ? 0 : 1;
    }
    private sealed class LimitedImport(IPlaceDiscoveryProvider inner,int limit):IPlaceDiscoveryProvider
    {
        public string Name=>inner.Name;public bool IsConfigured=>inner.IsConfigured;
        public async Task<PlaceSearchResult> SearchAsync(string query,string? city,CancellationToken ct)
        {var source=await inner.SearchAsync(query,city,ct);return source with{Places=source.Places.OrderBy(p=>p.ExternalId,StringComparer.Ordinal).Take(limit).ToArray()};}
        public Task<DiscoveredPlace?> GetDetailsAsync(string id,CancellationToken ct)=>inner.GetDetailsAsync(id,ct);
    }

}
