using System;
using System.IO;
using System.Linq;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class CuiaExperienceV5Tests
{
    [Fact]
    public void DapperTypeMap_MatchNamesWithUnderscores_MustBeTrue()
    {
        // Força a execução do static constructor da fábrica usando connection string válida
        _ = new MySqlConnectionFactory("Server=db.example.com; Database=belrestaurante; Uid=belrest; Pwd=123; SslMode=VerifyFull;");

        // Frente 1: Mapeamento de snake_case para propriedades PascalCase no Dapper
        Assert.True(Dapper.DefaultTypeMap.MatchNamesWithUnderscores);
    }

    [Fact]
    public void Migrations_MustContain23Scripts_SequentialAndValid()
    {
        // Frente 1 & 2: Todas as migrações embutidas devem ser encontradas e ordenadas
        var scripts = MigrationRunner.LoadScripts();
        Assert.Equal(23, scripts.Count);

        for (int i = 0; i < scripts.Count; i++)
        {
            var expectedNumber = (i + 1).ToString("D3");
            Assert.Equal(expectedNumber, scripts[i].Id);
            Assert.False(string.IsNullOrWhiteSpace(scripts[i].Sql));
        }

        Assert.Equal("019", scripts[18].Id);
    }

    [Fact]
    public void ParaOfficialCatalog_Belem_IsPresentAndValid()
    {
        // Frente 2: Belém como capital gastronômica oficial (IBGE 1501402)
        var belem = ParaOfficialCatalog.Municipalities.FirstOrDefault(m => m.IbgeCode == 1501402);
        Assert.NotNull(belem);
        Assert.Equal("Belém", belem.Name);
        Assert.Equal("belem", belem.NormalizedName);
    }

    [Fact]
    public void CuiaV5DesignSystem_CssTokens_MustBePresent()
    {
        // Frente 3: Arquivo cuia-v5.css deve conter tokens essenciais de design amazônico
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "src", "BelemRestaurantes.Web")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);
        var cssPath = Path.Combine(currentDir.FullName, "src", "BelemRestaurantes.Web", "wwwroot", "css", "cuia-v5.css");
        Assert.True(File.Exists(cssPath), $"Arquivo cuia-v5.css não encontrado em: {cssPath}");

        var cssContent = File.ReadAllText(cssPath);
        Assert.Contains("--cuia-forest: #113F32;", cssContent);
        Assert.Contains("--cuia-tucupi: #E8AD46;", cssContent);
        Assert.Contains("--cuia-urucum: #B7513C;", cssContent);
        Assert.Contains(".cuia-hero", cssContent);
        Assert.Contains(".cuia-establishment-card", cssContent);
    }
}
