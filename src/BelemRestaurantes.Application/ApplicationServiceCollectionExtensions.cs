using BelemRestaurantes.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra os casos de uso. Só recebe as abstrações; a implementação de
    /// persistência, HTTP e IA entra por <c>AddInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<DiscoveryOrchestrator>();
        services.AddScoped<ConsistencyChecker>();
        services.AddSingleton<IOptionsSnapshotLike, ProviderChainOptions>();
        services.AddSingleton<IAiRouter, AiRouter>();
        return services;
    }
}

/// <summary>Ordem de preferência dos provedores de IA, lida da configuração.</summary>
public sealed class ProviderChainOptions : IOptionsSnapshotLike
{
    public IReadOnlyList<AiProvider> ProviderChain { get; init; } = [AiProvider.Gemini, AiProvider.Groq, AiProvider.DeepSeek];
}