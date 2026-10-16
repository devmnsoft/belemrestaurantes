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
    public static string? ConnectionString() =>
        Environment.GetEnvironmentVariable("BELRESTAURANTES_TEST_MYSQL")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__MySql");
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
                ["ConnectionStrings:MySql"] = cs
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}