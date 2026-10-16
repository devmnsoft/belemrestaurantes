using Xunit;
using Microsoft.Extensions.DependencyInjection;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public class MigrationTests
{
    [Fact]
    public async Task Apply_Migrations_To_MySql()
    {
        var connection = DatabaseEnvironment.ConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(connection), "BELRESTAURANTES_TEST_MYSQL obrigatório: integração não foi homologada.");

        var provider = ServiceContainer.Build();
        var runner = provider.GetRequiredService<MigrationRunner>();

        var result = await runner.ApplyAsync(default);

        Assert.NotNull(result);
        Assert.Equal(MigrationRunner.LoadScripts().Count, result.Applied + result.AlreadyCurrent.Count);
    }

    [Fact]
    public async Task Apply_Migrations_Idempotent()
    {
        var connection = DatabaseEnvironment.ConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(connection), "BELRESTAURANTES_TEST_MYSQL obrigatório: integração não foi homologada.");

        var provider = ServiceContainer.Build();
        var runner = provider.GetRequiredService<MigrationRunner>();

        // Segunda aplicação deve ser no-op.
        await runner.ApplyAsync(default);
        var second = await runner.ApplyAsync(default);

        Assert.Equal(0, second.Applied);
        Assert.Equal(MigrationRunner.LoadScripts().Count, second.AlreadyCurrent.Count);
    }
}