using BelemRestaurantes.Application;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;

namespace BelemRestaurantes.IntegrationTests;

public class DatabaseEnvironment
{
    public static string MailKeys => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "belemrestaurantes-test-mail", Environment.ProcessId.ToString());

    public static string? ConnectionString() =>
        Environment.GetEnvironmentVariable("BELRESTAURANTES_TEST_MYSQL");
}

public static class ServiceContainer
{
    public static IServiceProvider Build()
    {
        var cs = DatabaseEnvironment.ConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("BELRESTAURANTES_TEST_MYSQL não definido.");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = cs,
                ["MailEncryption:KeyRingPath"] = DatabaseEnvironment.MailKeys
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}