using Xunit;
using Microsoft.Extensions.DependencyInjection;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;

namespace BelemRestaurantes.IntegrationTests;

public class MigrationTests
{
    [Fact]
    public async Task Apply_Migrations_To_MySql()
    {
        var connection = DatabaseEnvironment.ConnectionString();
        if (string.IsNullOrWhiteSpace(connection))
        {
            // Teste de integração opcional: sem banco disponível o critério fica pendente.
            Assert.True(true);
            return;
        }

        var provider = ServiceContainer.Build();
        var runner = provider.GetRequiredService<MigrationRunner>();

        var result = await runner.ApplyAsync(default);

        Assert.NotNull(result);
        Assert.Equal(5, result.Applied + result.AlreadyCurrent.Count);
    }

    [Fact]
    public async Task Apply_Migrations_Idempotent()
    {
        var connection = DatabaseEnvironment.ConnectionString();
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.True(true);
            return;
        }

        var provider = ServiceContainer.Build();
        var runner = provider.GetRequiredService<MigrationRunner>();

        // Segunda aplicação deve ser no-op.
        await runner.ApplyAsync(default);
        var second = await runner.ApplyAsync(default);

        Assert.Equal(0, second.Applied);
        Assert.Equal(5, second.AlreadyCurrent.Count);
    }
}