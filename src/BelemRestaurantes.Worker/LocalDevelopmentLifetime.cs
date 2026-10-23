using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Worker;

/// <summary>Allows the local supervisor to request graceful shutdown without killing in-flight jobs.</summary>
public sealed class LocalDevelopmentLifetime(IHostEnvironment environment,IConfiguration configuration,IHostApplicationLifetime lifetime):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path=configuration["Worker:StopFile"];
        if(!environment.IsDevelopment() || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))return;
        try
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                if(File.Exists(path)){lifetime.StopApplication();return;}
                await Task.Delay(2000,stoppingToken);
            }
        }
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){ }
    }
}
