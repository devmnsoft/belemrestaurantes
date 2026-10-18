using BelemRestaurantes.Application;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Ai;
using BelemRestaurantes.Infrastructure.Google;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    internal const string GroqKey = "groq";
    internal const string DeepSeekKey = "deepseek";
    internal const string GeminiKey = "gemini";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Dapper.SqlMapper.AddTypeHandler(new MySql.UtcDateTimeOffsetHandler());
        services.Configure<GooglePlacesOptions>(configuration.GetSection(GooglePlacesOptions.SectionName));

        var googleOptions = configuration
            .GetSection(GooglePlacesOptions.SectionName)
            .Get<GooglePlacesOptions>() ?? new GooglePlacesOptions();
        googleOptions.ApiKey = Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY") ?? googleOptions.ApiKey;
        services.AddSingleton(googleOptions);

        services.AddScoped<IOpenDataCache, MySqlOpenDataCache>();
        services.AddSingleton(sp=>new GooglePlacesClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("googleplaces"),sp.GetRequiredService<GooglePlacesOptions>(),sp.GetRequiredService<IClock>(),sp.GetRequiredService<ILogger<GooglePlacesClient>>()));
        services.AddSingleton(sp=>new OpenData.OpenStreetMapDiscoveryClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("opendata"),new MySqlOpenDataCache(sp.GetRequiredService<MySqlConnectionFactory>()),sp.GetRequiredService<IClock>(),sp.GetRequiredService<ILogger<OpenData.OpenStreetMapDiscoveryClient>>()));
        services.AddSingleton<IPlaceDiscoveryProvider>(sp => configuration["Discovery:Provider"] == "OpenStreetMap"
            ? new OpenData.OpenStreetMapDiscoveryClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("opendata"),
                // Cache não mantém conexões abertas; a fábrica é singleton.
                new MySqlOpenDataCache(sp.GetRequiredService<MySqlConnectionFactory>()),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<OpenData.OpenStreetMapDiscoveryClient>>())
            : new GooglePlacesClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("googleplaces"),
            sp.GetRequiredService<GooglePlacesOptions>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<GooglePlacesClient>>()));

        // Provedores de IA: configuração com chave por provedor.
        services.AddKeyedSingleton<AiProviderOptions>(GroqKey, (_, _) => BuildProviderOptions(
            configuration, "Groq", "https://api.groq.com/openai/v1/", "llama-3.3-70b-versatile", 80, 300));
        services.AddKeyedSingleton<AiProviderOptions>(DeepSeekKey, (_, _) => BuildProviderOptions(
            configuration, "DeepSeek", "https://api.deepseek.com/v1/", "deepseek-chat", 140, 280));
        services.AddKeyedSingleton<AiProviderOptions>(GeminiKey, (_, _) => BuildProviderOptions(
            configuration, "Gemini", "https://generativelanguage.googleapis.com/v1beta/", "gemini-2.0-flash", 10, 40));

        services.AddSingleton<GeminiProvider>(sp => new GeminiProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"),
            sp.GetRequiredKeyedService<AiProviderOptions>(GeminiKey)));
        services.AddSingleton<GroqProvider>(sp => new GroqProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"),
            sp.GetRequiredKeyedService<AiProviderOptions>(GroqKey)));
        services.AddSingleton<DeepSeekProvider>(sp => new DeepSeekProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"),
            sp.GetRequiredKeyedService<AiProviderOptions>(DeepSeekKey)));

        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<GroqProvider>());
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<DeepSeekProvider>());

        services.AddSingleton<IOptionsSnapshotLike>(_ => new ProviderChainOptions
        {
            ProviderChain = ReadProviderChain(configuration)
        });

        // Resiliência.
        services.AddSingleton<IRetryPolicy>(new ExponentialBackoffPolicy(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromSeconds(60)));
        services.AddSingleton(new CircuitBreaker(
            failureThreshold: 5,
            breakDuration: TimeSpan.FromMinutes(2)));
        services.AddSingleton<ResilientExecutor>();

        services.AddSingleton<Members.MailPayloadProtector>();
        services.AddSingleton<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        services.AddScoped<IMemberAccounts, Members.MySqlMemberAccounts>();
        services.AddSingleton<Members.ISmtpTransportFactory, Members.DefaultSmtpTransportFactory>();
        services.AddScoped<ITransactionalMailSender, Members.SmtpTransactionalMailSender>();
        services.AddScoped<IEmailDispatcher, Members.MySqlEmailDispatcher>();

        // Persistência.
        services.AddSingleton(sp =>
        {
            var connectionString = configuration.GetConnectionString("MySql");
            return new MySqlConnectionFactory(connectionString ?? string.Empty,
                sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()?.EnvironmentName);
        });

        services.AddScoped<IRestaurantReadRepository, MySqlRestaurantReadRepository>();
        services.AddScoped<ISuggestionRepository, MySqlSuggestionRepository>();
        services.AddScoped<IRestaurantIngestionRepository, MySqlRestaurantIngestionRepository>();
        services.AddScoped<IModerationRepository, MySqlModerationRepository>();
        services.AddScoped<MySqlBulkModeration>();
        services.AddScoped<IOperatingHoursRepository,MySqlOperatingHours>();
        services.AddScoped<IMobilityProvider,Mobility.GoogleRoutesProvider>();
        services.AddHttpClient("routes",client=>client.Timeout=TimeSpan.FromSeconds(15));
        services.AddScoped<IDiscoveryQueue, MySqlDiscoveryQueue>();
        services.AddScoped<IDiscoveryTargetRepository, MySqlDiscoveryTargetRepository>();
        services.AddScoped<IFavoriteRepository, MySqlFavoriteRepository>();
        services.AddScoped<IRatingRepository, MySqlRatingRepository>();
        services.AddScoped<IDeletionRequestRepository, MySqlDeletionRequestRepository>();
        services.AddScoped<ICycleTelemetryRepository, MySqlCycleTelemetryRepository>();
        services.AddSingleton<ICostLedger, MySqlCostLedger>();
        services.AddScoped<IAuditWriter, MySqlAuditWriter>();
        services.AddScoped<MigrationRunner>();

        // Sprint 04 - Módulos Sociais, Fotos, Selo Cuia e Governança LGPD
        services.AddScoped<IExperienceService, Social.MySqlExperienceService>();
        services.AddScoped<IPhotoStorageService, Social.DiskPhotoStorageService>();
        services.AddScoped<ICuiaService, Social.MySqlCuiaService>();
        services.AddScoped<ILgpdService, Social.MySqlLgpdService>();

        // Sprint 05 - Portal do Restaurante Parceiro, Cardápio Digital Inteligente, Monetização e Roteiros
        services.AddScoped<IPartnerAuthorizationService, Partner.MySqlPartnerAuthorizationService>();
        services.AddScoped<IPartnerPortalService, Partner.MySqlPartnerPortalService>();
        services.AddScoped<IMenuService, Partner.MySqlMenuService>();
        services.AddScoped<IMonetizationService, Partner.MySqlMonetizationService>();
        services.AddScoped<IMobilityTrailService, Partner.MySqlMobilityTrailService>();

        // Sprint 06 - Busca Gastronômica Inteligente e Autocomplete
        services.AddScoped<ISearchService, Search.MySqlSearchService>();

        // Sprint 08 - Cuia Crawler: Pesquisa autônoma na internet e enriquecimento seguro do catálogo
        services.AddSingleton<ISafeHttpClient, Crawler.SafeHttpClient>();
        services.AddSingleton<IRobotsTxtService, Crawler.RobotsTxtService>();
        services.AddSingleton<ICrawlerParser, Crawler.CrawlerParser>();
        services.AddScoped<ICrawlerSourceRepository, Crawler.MySqlCrawlerSourceRepository>();
        services.AddScoped<ICrawlerTaskQueue, Crawler.MySqlCrawlerTaskQueue>();
        services.AddScoped<ICrawlerTelemetryRepository, Crawler.MySqlCrawlerTelemetryRepository>();
        services.AddScoped<ICrawlerOrchestrator, Application.Services.CrawlerOrchestrator>();

        // Sprint 09 - Cuia Discovery 360: Cobertura Estadual, Enriquecimento e Motor de Mídia
        services.AddScoped<IGeoMunicipalityRepository, MySql.MySqlGeoMunicipalityRepository>();
        services.AddScoped<IEnrichmentQueueRepository, MySql.MySqlEnrichmentQueueRepository>();
        services.AddScoped<IRestaurantAttributeHistoryRepository, MySql.MySqlRestaurantAttributeHistoryRepository>();
        services.AddScoped<IRestaurantMediaRepository, MySql.MySqlRestaurantMediaRepository>();
        services.AddScoped<IEnrichmentService, Crawler.EnrichmentService>();

        // Sprint 10 - Cuia Discovery Intelligence, Distritos e Monetização Real
        services.AddScoped<IGeoDistrictLocalityRepository, MySql.MySqlDistrictLocalityRepository>();
        services.AddScoped<IDiscoveryIntelligenceQueue, MySql.MySqlDiscoveryIntelligenceQueue>();
        services.AddScoped<ICommercialMonetizationRepository, MySql.MySqlCommercialMonetizationRepository>();
        services.AddScoped<IDiscoveryIntelligenceOrchestrator, Crawler.DiscoveryIntelligenceOrchestrator>();

        // Sprint 11 - Telemetria Unificada e Qualidade do Catálogo Gastronômico
        services.AddScoped<IDiscoveryTelemetryRepository, MySql.MySqlDiscoveryTelemetryRepository>();

        services.AddHttpClient("googleplaces", client =>
        {
            client.Timeout = googleOptions.Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BelemRestaurantes/2.0 (+discovery)");
        });
        services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("opendata", client =>
        {
            var endpoint = new Uri(configuration["OpenData:Endpoint"] ?? "https://overpass-api.de/api/interpreter");
            if (endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new InvalidOperationException("OpenData:Endpoint exige HTTPS sem credenciais na URL.");
            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromSeconds(40);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BelemRestaurantes/3.0 (+https://belemrestaurantes.com.br)");
        });

        return services;
    }

    private static AiProviderOptions BuildProviderOptions(
        IConfiguration configuration,
        string sectionName,
        string defaultBaseUrl,
        string defaultModel,
        long inputMicrosPerThousand,
        long outputMicrosPerThousand)
    {
        var section = configuration.GetSection($"Ai:{sectionName}");

        return new AiProviderOptions
        {
            ApiKey = Environment.GetEnvironmentVariable($"{sectionName.ToUpperInvariant()}_API_KEY") ?? section["ApiKey"],
            BaseUrl = section["BaseUrl"] ?? defaultBaseUrl,
            Model = section["Model"] ?? defaultModel,
            InputMicrosPerThousand = inputMicrosPerThousand,
            OutputMicrosPerThousand = outputMicrosPerThousand,
            Timeout = TimeSpan.FromSeconds(section.GetValue("TimeoutSeconds", 30))
        };
    }

    private static IReadOnlyList<AiProvider> ReadProviderChain(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Ai:ProviderChain").Get<string[]>();

        if (configured is { Length: > 0 })
        {
            var parsed = configured
                .Select(value => Enum.TryParse<AiProvider>(value, ignoreCase: true, out var p) ? p : AiProvider.Disabled)
                .Where(p => p != AiProvider.Disabled)
                .Distinct()
                .ToArray();

            if (parsed.Length > 0)
            {
                return parsed;
            }
        }

        return [AiProvider.Gemini, AiProvider.Groq, AiProvider.DeepSeek];
    }
}
