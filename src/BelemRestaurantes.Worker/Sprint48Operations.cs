using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Worker;

internal static class Sprint48Operations
{
    public static async Task ImportReferenceAsync(IServiceProvider services,string path)
    {
        var file=new FileInfo(Path.GetFullPath(path));
        if(file.Length>32768)throw new ArgumentException("Arquivo de referência acima do limite.");
        var reference=JsonSerializer.Deserialize<PlaceReference>(await File.ReadAllTextAsync(file.FullName),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new ArgumentException("Referência inválida.");
        using var scope=services.CreateScope();
        var id=await scope.ServiceProvider.GetRequiredService<IPlaceReferences>().ObserveAsync(reference,default);
        Console.WriteLine(JsonSerializer.Serialize(new{id,observed=true}));
    }

    public static async Task ApproveReferenceAsync(IServiceProvider services,long id)
    {
        using var scope=services.CreateScope();
        var changed=await scope.ServiceProvider.GetRequiredService<IPlaceReferences>().ReviewAsync(id,"approved","local-operator:sprint48",default);
        Console.WriteLine(JsonSerializer.Serialize(new{id,changed}));
    }

    public static async Task ProbeFreeAiAsync(IServiceProvider services,string placeName)
    {
        using var scope=services.CreateScope();
        var reference=(await scope.ServiceProvider.GetRequiredService<IPlaceReferences>().FindAsync(placeName,null,default)).FirstOrDefault();
        if(reference is null){Console.WriteLine("Nenhuma referência revisada para o teste; nenhuma chamada feita.");return;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(32));
        var result=await scope.ServiceProvider.GetRequiredService<IAiRouter>().ClassifyAsync("reference-homologation",
            JsonSerializer.Serialize(new{nome=reference.PlaceName,cidade=reference.City,fonte=reference.SourceUrl,pratos_mencionados=reference.MenuItems}),timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(new{generated=result is not null,classification=result}));
    }

    public static async Task ScheduleBelemAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var changed=await db.ExecuteAsync("UPDATE discovery_targets SET provider='openstreetmap',refresh_minutes=1440,result_limit=100,next_run_at=LEAST(next_run_at,UTC_TIMESTAMP(6)) WHERE normalized_city='belem' AND enabled=TRUE AND provider IN ('default','openstreetmap')");
        var queued=await scope.ServiceProvider.GetRequiredService<IDiscoveryQueue>().EnqueueDueTargetsAsync(DateTimeOffset.UtcNow,default);
        Console.WriteLine(JsonSerializer.Serialize(new{changed,queued,note="Somente alvos já habilitados; pausas e orçamentos preservados."}));
    }

    public static async Task PrintAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var targetUri=new Uri("https://generativelanguage.googleapis.com/");
        var proxy=HttpClient.DefaultProxy.GetProxy(targetUri);
        var usesSystemProxy=proxy is not null && proxy.Host!=targetUri.Host;
        var heartbeats=(await db.QueryAsync("SELECT instance_id,started_at,heartbeat_at,state,discovery_enabled,crawler_enabled FROM worker_heartbeats ORDER BY heartbeat_at DESC LIMIT 3")).ToArray();
        var catalog=(await db.QueryAsync("SELECT status,COUNT(*) total FROM restaurants GROUP BY status")).ToArray();
        var quality=await db.QuerySingleAsync("SELECT COUNT(*) records,SUM(NULLIF(TRIM(address),'') IS NOT NULL) with_address,SUM(latitude IS NOT NULL AND longitude IS NOT NULL) with_coordinates FROM restaurants WHERE normalized_city='belem'");
        var targets=(await db.QueryAsync("SELECT id,city,category,provider,enabled,next_run_at,last_success_at,result_offset,result_more,result_source_total,result_snapshot_at FROM discovery_targets WHERE city='Belém' ORDER BY id LIMIT 20")).ToArray();
        var queues=(await db.QueryAsync("SELECT state,COUNT(*) total FROM discovery_jobs GROUP BY state")).ToArray();
        var sources=(await db.QueryAsync("SELECT status,COUNT(*) total FROM crawler_sources GROUP BY status")).ToArray();
        var budget=(await db.QueryAsync("SELECT provider,paused,monthly_limit_micros_usd FROM api_budgets ORDER BY provider")).ToArray();
        var freeUsage=(await db.QueryAsync("SELECT service,period,requests,last_used_at FROM external_free_usage WHERE service IN ('cuia_gemini','worker_gemini','cuia_groq','worker_groq','cuia_drael') ORDER BY service,period")).ToArray();
        var identityAudit=await db.QuerySingleAsync("SELECT COUNT(*) accounts,SUM(cpf_digest IS NULL) legacy_without_document FROM member_users");
        var duplicates=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT normalized_email FROM member_users GROUP BY normalized_email HAVING COUNT(*)>1) d");
        Console.WriteLine(JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,usesSystemProxy,heartbeats,catalog,quality,targets,queues,sources,budget,freeUsage,identityAudit,duplicateEmails=duplicates},new JsonSerializerOptions{WriteIndented=true}));
    }
}
