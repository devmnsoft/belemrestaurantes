using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

/// <summary>Registra apenas presença dos segredos, nunca seu conteúdo.</summary>
public sealed class ConfigurationDiagnosticsService(IConfiguration configuration,
    IHostEnvironment environment, ILogger<ConfigurationDiagnosticsService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker independente da Web. Ambiente={Environment}; conteúdo={ContentRoot}",
            environment.EnvironmentName, environment.ContentRootPath);
        foreach (var key in new[] { "ConnectionStrings:MySql", "GooglePlaces:ApiKey", "Ai:Gemini:ApiKey", "Ai:Groq:ApiKey", "Ai:DeepSeek:ApiKey" })
            logger.LogInformation("Configuração {Key}: {State}", key,
                string.IsNullOrWhiteSpace(configuration[key]) ? "ausente" : "presente (valor protegido)");
        var placesKey = configuration["GooglePlaces:ApiKey"]?.Trim();
        logger.LogInformation("Fonte de descoberta selecionada: {Source}", configuration["Discovery:Provider"] ?? "GooglePlaces");
        if (placesKey?.StartsWith("GOCSPX-", StringComparison.Ordinal) == true)
            logger.LogWarning("GooglePlaces:ApiKey contém um segredo OAuth; Google Places continua indisponível. A fonte alternativa selecionada pode operar sem essa credencial.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
