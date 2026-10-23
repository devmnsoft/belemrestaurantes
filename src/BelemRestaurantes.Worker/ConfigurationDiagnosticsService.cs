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
        // Sprint 68 — a flag de migrações pode ser silenciosamente sobrescrita por user-secrets locais,
        // variável de ambiente ou linha de comando (precedência acima dos appsettings). Se o valor efetivo
        // divergir do declarado nos appsettings, registre um aviso com os dois valores (não é segredo).
        var effectiveMigrationsFlag = configuration.GetValue<bool?>("Database:ApplyMigrations");
        var appSettingsBaseline = new ConfigurationBuilder()
            .SetBasePath(environment.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .Build();
        var declaredMigrationsFlag = appSettingsBaseline.GetValue<bool?>("Database:ApplyMigrations");
        if (IsValueOverridden(effectiveMigrationsFlag, declaredMigrationsFlag))
        {
            logger.LogWarning(
                "Database:ApplyMigrations está sendo sobrescrito por fonte de maior precedência (user-secrets, variável de ambiente ou linha de comando): appsettings={AppSettings}, efetivo={Effective}. Confira 'dotnet user-secrets list --id belemrestaurantes-worker'.",
                declaredMigrationsFlag, effectiveMigrationsFlag);
        }
        return Task.CompletedTask;
    }

    /// <summary>Sprint 68 — verdadeiro quando o valor efetivo difere do declarado nos appsettings: alguma
    /// fonte de maior precedência (user-secrets, variável de ambiente, linha de comando) o sobrescreveu.
    /// Valor ausente em qualquer lado nunca é considerado sobrescrita.</summary>
    public static bool IsValueOverridden(bool? effective, bool? appSettingsValue) =>
        effective.HasValue && appSettingsValue.HasValue && effective.Value != appSettingsValue.Value;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
