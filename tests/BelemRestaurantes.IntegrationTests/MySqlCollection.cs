using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Serializa os testes que escrevem no banco compartilhado (coleção "MySql") e
/// garante a existência de fichas publicadas genéricas para os testes que apenas escolhem
/// uma ficha existente — um banco limpo (CI) não traz fichas. As linhas são recriadas de
/// forma idempotente antes da coleção e removidas ao final.</summary>
[CollectionDefinition("MySql")]
public sealed class MySqlCollection : ICollectionFixture<MySqlCatalogFixture>
{
}

/// <summary>Fixture de catálogo mínimo: três fichas publicadas (sem coordenadas), em Belém e
/// no interior, que qualquer teste da coleção pode usar como alvo de escolha.</summary>
public sealed class MySqlCatalogFixture : IAsyncLifetime
{
    internal const string NameList = "'CI Fixture Restaurante','CI Fixture Hamburgueria','CI Fixture Pizzaria'";

    public async Task InitializeAsync()
    {
        await using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        await db.ExecuteAsync($"DELETE FROM restaurants WHERE name IN ({NameList})");
        await db.ExecuteAsync("""
            INSERT INTO restaurants(name, normalized_name, city, normalized_city, address, category, status)
            VALUES
              ('CI Fixture Restaurante', 'ci fixture restaurante', 'Belém', 'belem', 'RUA DA CI FIXTURE, 10', 'Restaurante', 'published'),
              ('CI Fixture Hamburgueria', 'ci fixture hamburgueria', 'Belém', 'belem', 'RUA DA CI FIXTURE, 20', 'Hamburgueria', 'published'),
              ('CI Fixture Pizzaria', 'ci fixture pizzaria', 'Marabá', 'maraba', 'RUA DA CI FIXTURE, 300', 'Pizzaria', 'published')
            """);
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var root = (ServiceProvider)ServiceContainer.Build();
            await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
            await db.ExecuteAsync($"DELETE FROM restaurants WHERE name IN ({NameList})");
        }
        catch
        {
            // Limpeza é melhor esforço; linhas órfãs não afetam a próxima execução.
        }
    }
}
