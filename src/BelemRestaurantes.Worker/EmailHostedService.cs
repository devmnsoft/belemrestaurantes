using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Worker;

public sealed class EmailHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<EmailHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Smtp:Enabled")) return;
        if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"]) || string.IsNullOrWhiteSpace(configuration["Smtp:Username"]) ||
            string.IsNullOrWhiteSpace(configuration["Smtp:Password"]) || string.IsNullOrWhiteSpace(configuration["MailEncryption:KeyRingPath"]))
            throw new InvalidOperationException("Enabled SMTP requires private credentials and a shared mail key ring before consuming the outbox.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, configuration.GetValue("Smtp:IntervalSeconds", 30))));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(Math.Clamp(configuration.GetValue("Smtp:BatchSize", 5), 1, 20), stoppingToken);
                if (processed > 0) logger.LogInformation("Transactional mail batch processed: {Count}", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Transactional mail batch failed: {ErrorType}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
