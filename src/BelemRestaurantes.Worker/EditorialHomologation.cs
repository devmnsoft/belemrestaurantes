using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace BelemRestaurantes.Worker;

internal static class EditorialHomologation
{
    private static void LocalOnly(IServiceProvider services,bool authorized)
    {
        var host=services.GetRequiredService<IHostEnvironment>();
        var cs=new MySqlConnectionStringBuilder(services.GetRequiredService<IConfiguration>().GetConnectionString("MySql")??throw new InvalidOperationException("Conexão não configurada."));
        if(!OperatingSystem.IsWindows()||!host.IsDevelopment()||!authorized||cs.Server is not("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Operação editorial local exige Windows, Development, banco loopback e autorização explícita.");
    }
    public static async Task SeedAsync(IServiceProvider services,string[] args)
    {
        LocalOnly(services,args.Contains("--authorize-editorial",StringComparer.Ordinal));
        using var scope=services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var repository=scope.ServiceProvider.GetRequiredService<IParaRecipes>();
        var assembly=typeof(MySqlParaRecipes).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("Editorial.para-recipes.json",StringComparison.Ordinal)))!;
        var recipes=await JsonSerializer.DeserializeAsync<ParaRecipe[]>(stream)??[];
        var created=0;var preserved=0;
        foreach(var recipe in recipes)
        {
            if(await repository.GetAsync(recipe.Slug,true,default)is not null){preserved++;continue;}
            await repository.SaveAsync(recipe,"local:editorial-bootstrap",default);created++;
        }
        foreach(var relation in args.Where(a=>a.StartsWith("--relate=",StringComparison.Ordinal)))
        {
            var parts=relation[9..].Split(':',2);
            if(parts.Length!=2||!long.TryParse(parts[1],out var id)||id<1)throw new ArgumentException("Use --relate=slug:identificador-publicado.");
            var recipe=await repository.GetAsync(parts[0],true,default)??throw new ArgumentException("Receita inexistente.");
            if(!recipe.RestaurantIds.Contains(id)){recipe.RestaurantIds=recipe.RestaurantIds.Append(id).ToArray();await repository.SaveAsync(recipe,"local:editorial-curation",default);}
        }
        Console.WriteLine($"Receitas editoriais: {created} publicadas, {preserved} preservadas. Adaptações identificadas; sem alegação de receita oficial de restaurante.");
    }
    public static async Task ConfigureCoverageAsync(IServiceProvider services,string[] args)
    {
        LocalOnly(services,args.Contains("--authorize-statewide-discovery",StringComparer.Ordinal));
        using var scope=services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var count=await scope.ServiceProvider.GetRequiredService<MySqlStatewideDiscoveryPlan>().ConfigureAsync("local:authorized-coverage",default);
        Console.WriteLine($"Plano OpenStreetMap ativo: {count} municípios oficiais, atualização semanal, primeira passagem escalonada. Distritos de Belém permanecem sob o município Belém.");
    }
    public static async Task QueueChecksAsync(IServiceProvider services,string[] args)
    {
        LocalOnly(services,args.Contains("--authorize-maintenance-checks",StringComparer.Ordinal));
        using var scope=services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var jobs=scope.ServiceProvider.GetRequiredService<MySqlMaintenanceJobs>();
        foreach(var kind in new[]{"hours","mobility"})Console.WriteLine($"Verificação local enfileirada: {kind}, tarefa {await jobs.EnqueueAsync(kind,"local:authorized-maintenance",default)}. Resultado será produzido pelo Worker.");
    }
    private sealed record CuratedHours(long RestaurantId,string SourceUrl,OpeningDay[] Days);
    public static async Task CurateHoursAsync(IServiceProvider services,string[] args)
    {
        LocalOnly(services,args.Contains("--authorize-hours-curation",StringComparer.Ordinal));
        var path=args.First(a=>a.StartsWith("--hours-file=",StringComparison.Ordinal))[13..];
        if(new FileInfo(path).Length>65536)throw new ArgumentException("Arquivo de curadoria excede 64 KB.");
        var entries=JsonSerializer.Deserialize<CuratedHours[]>(await File.ReadAllTextAsync(path))??[];
        if(entries.Length is <1 or >10)throw new ArgumentException("Revise de um a dez estabelecimentos publicados.");
        using var scope=services.CreateScope();
        var restaurants=scope.ServiceProvider.GetRequiredService<IRestaurantReadRepository>();
        var hours=scope.ServiceProvider.GetRequiredService<IOperatingHoursRepository>();
        foreach(var entry in entries)
        {
            var restaurant=await restaurants.GetPublishedAsync(entry.RestaurantId,default)??throw new ArgumentException("Estabelecimento não publicado.");
            var current=await hours.GetAsync(entry.RestaurantId,default);
            if(current.Info?.Authority=="owner")throw new ArgumentException("O horário do proprietário deve ser preservado.");
            var now=DateTime.UtcNow;
            await hours.SaveAsync(entry.RestaurantId,current.Info?.Version??0,new(){Authority="external",SourceUrl=entry.SourceUrl,ConfirmedAt=now,ExpiresAt=now.AddDays(7)},entry.Days,"local:authorized-hours-curation",default);
            Console.WriteLine($"Horário de {restaurant.Name} registrado com fonte, validade de sete dias e histórico de versões.");
        }
    }
    public static async Task ReviewAsync(IServiceProvider services,string[] args,bool publish)
    {
        LocalOnly(services,!publish||args.Contains("--authorize-catalog-publication",StringComparer.Ordinal));
        using var scope=services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var moderation=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();
        if(!publish)
        {
            var term=args.FirstOrDefault(a=>a.StartsWith("--term=",StringComparison.Ordinal))?[7..];
            var source=args.FirstOrDefault(a=>a.StartsWith("--source=",StringComparison.Ordinal))?[9..];
            var page=await moderation.ListAsync(new(Term:term,City:"Belém",Source:source,Completeness:args.Contains("--include-incomplete",StringComparer.Ordinal)?null:"eligible",Sort:"name"),1,default);
            Console.WriteLine(JsonSerializer.Serialize(page.Items.Take(20).Select(r=>new{r.Id,r.Name,r.City,r.Category,r.Address,r.SourceUrl,r.ExternalId,r.Stage,r.BlockReason}),new JsonSerializerOptions{WriteIndented=true}));return;
        }
        var text=args.FirstOrDefault(a=>a.StartsWith("--ids=",StringComparison.Ordinal))?[6..]??"";
        var ids=text.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).Distinct().ToArray();
        if(ids.Length is <1 or >10||ids.Any(id=>id<1))throw new ArgumentException("Revise e informe de um a dez identificadores reais.");
        var summaries=new List<object>();
        foreach(var phase in new[]{("approve_only","pending"),("publish","approved")})
        {
            var filter=new ModerationFilter(Stage:phase.Item2,Completeness:"eligible");
            // Seleção explícita de IDs; o serviço repete as validações sob bloqueio antes de publicar.
            ModerationBatch batch;
            try{batch=await moderation.PrepareAsync("local:authorized-curation",phase.Item1,null,ids,false,filter,default);}
            catch(ArgumentException ex)when(ex.Message.StartsWith("Selecione entre",StringComparison.Ordinal)){continue;}
            var result=await moderation.ExecuteAsync(batch.Id,"local:authorized-curation",default);
            summaries.Add(new{batchId=batch.Id,operation=phase.Item1,results=result?.Results});
        }
        Console.WriteLine(JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
    }
}
