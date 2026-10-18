using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

public sealed class WorkerHealthService(IServiceScopeFactory scopes,IConfiguration config,ILogger<WorkerHealthService> logger):BackgroundService
{
    public static string InstanceId { get; }=$"{Environment.MachineName}-{Environment.ProcessId}";
    private readonly DateTime started=DateTime.UtcNow;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try { await WriteAsync("running",stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Não foi possível gravar heartbeat; nova tentativa em 20 segundos.");}
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        try{await WriteAsync("stopped",ct);}catch(Exception ex){logger.LogWarning(ex,"Heartbeat final indisponível.");}
    }
    private async Task WriteAsync(string state,CancellationToken ct)
    {
        using var scope=scopes.CreateScope();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO worker_heartbeats(instance_id,started_at,heartbeat_at,working_set_bytes,state,discovery_enabled,crawler_enabled)
            VALUES(@id,@started,UTC_TIMESTAMP(6),@memory,@state,@discovery,@crawler)
            ON DUPLICATE KEY UPDATE heartbeat_at=UTC_TIMESTAMP(6),working_set_bytes=@memory,state=@state,
              discovery_enabled=@discovery,crawler_enabled=@crawler
            """,new{id=InstanceId,started,memory=Environment.WorkingSet,state,discovery=config.GetValue<bool>("Discovery:Enabled"),crawler=config.GetValue<bool>("Crawler:Enabled")},cancellationToken:ct));
    }
}

public sealed class WorkerFileLoggerProvider(string directory):ILoggerProvider
{
    private readonly string logDirectory=directory;
    private readonly object gate=new();
    public ILogger CreateLogger(string categoryName)=>new FileLogger(this,categoryName);
    public void Dispose() { }
    private sealed class FileLogger(WorkerFileLoggerProvider owner,string category):ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Information;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
        {
            if(!IsEnabled(level))return;
            lock(owner.gate)
            {
                try
                {
                    Directory.CreateDirectory(owner.logDirectory);
                    var path=Path.Combine(owner.logDirectory,$"worker-{DateTime.UtcNow:yyyyMMdd}-{Environment.ProcessId}.ndjson");
                    if(File.Exists(path)&&new FileInfo(path).Length>20_000_000)return;
                    File.AppendAllText(path,System.Text.Json.JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,level=level.ToString(),category,message=formatter(state,exception)})+Environment.NewLine);
                }
                catch(IOException){Console.Error.WriteLine("Log persistente indisponível: confira espaço em disco e Worker:LogDirectory.");}
                catch(UnauthorizedAccessException){Console.Error.WriteLine("Log persistente sem permissão: confira a identidade do serviço e Worker:LogDirectory.");}
            }
        }
    }
}
