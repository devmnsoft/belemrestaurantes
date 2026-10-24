using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 78 — geo_municipalities passa a refletir exatamente o registro oficial IBGE do Pará:
// os 4 códigos duplicados (ausentes do registro) somem, cada cidade resolve para o código
// oficial único, e as localidades referenciam municípios que existem (a regra da casa: o que
// não existe nem aparece). Os códigos oficiais foram validados em 24/10/2026 contra
// https://servicodados.ibge.gov.br/api/v1/localidades/estados/15/municipios (144 municípios).
[Collection("MySql")]
public sealed class Sprint78MunicipalityDedupTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString()!,
                ["Discovery:Enabled"] = "false",
                ["Crawler:Enabled"] = "false"
            })));

    private static async Task<MySqlConnection> OpenAsync(WebApplicationFactory<Program> app)
    {
        var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        return await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
    }

    [Fact]
    public async Task GeoMunicipalitiesMatchTheOfficialIbgeRegistryExactly()
    {
        using var app = Factory();
        await using var db = await OpenAsync(app);

        var total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geo_municipalities");
        Assert.True(total == 144, $"esperado 144 municípios oficiais (IBGE), encontrado {total}");

        // Nenhum código ausente do registro oficial sobrevive.
        var invalidCodes = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM geo_municipalities WHERE ibge_code IN (1506304, 1506353, 1506544, 1506585)");
        Assert.Equal(0, invalidCodes);

        // Nenhum nome se repete na tabela (a duplicata era por nome com código divergente).
        var duplicatedNames = await db.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM (SELECT normalized_name FROM geo_municipalities GROUP BY normalized_name HAVING COUNT(*) > 1) d""");
        Assert.Equal(0, duplicatedNames);

        // Cada uma das quatro cidades resolve para exatamente um código — o oficial.
        var rows = (await db.QueryAsync<(int, int)>(new CommandDefinition("""
            SELECT ibge_code, COUNT(*) FROM geo_municipalities
            WHERE normalized_name IN ('salvaterra', 'santa barbara do para', 'santa izabel do para', 'santa maria do para')
            GROUP BY ibge_code
            """))).AsList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(new HashSet<int> { 1506302, 1506351, 1506500, 1506609 }, rows.Select(r => r.Item1).ToHashSet());
        Assert.All(rows, r => Assert.Equal(1, r.Item2));
    }

    [Fact]
    public async Task LocalityReferencesPointAtMunicipalitiesThatExistAndAreCorrect()
    {
        using var app = Factory();
        await using var db = await OpenAsync(app);

        // Ninguém referencia mais um código que foi apagado.
        var danglingRefs = await db.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM geo_districts_localities l LEFT JOIN geo_municipalities m ON m.ibge_code = l.municipality_ibge WHERE m.id IS NULL""");
        Assert.Equal(0, danglingRefs);

        // Distritos de Salvaterra seguem com o código oficial (1506302), e nada fica no inválido (1506304).
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geo_districts_localities WHERE municipality_ibge = 1506304"));
        Assert.Equal(2, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM geo_districts_localities WHERE municipality_ibge = 1506302 AND normalized_name IN ('joanes', 'praia grande salvaterra')"));

        // As zonas turísticas de Salinópolis voltam para Salinópolis (1506203) — estavam em Santa Izabel do Pará (1506500).
        Assert.Equal(2, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM geo_districts_localities WHERE municipality_ibge = 1506203 AND normalized_name IN ('praia do atalaia', 'praia do farol velho')"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM geo_districts_localities WHERE municipality_ibge = 1506500 AND normalized_name IN ('praia do atalaia', 'praia do farol velho')"));
    }
}
