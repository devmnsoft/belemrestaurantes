using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 73 — Atlas Aberto: exportações públicas do catálogo publicado (GeoJSON/CSV) com números
// reconciliados contra SQL direto e página de contexto/licitação.
[Collection("MySql")]
public sealed class Sprint73OpenDataTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Sprint73OpenDataTests(WebApplicationFactory<Program> factory)
    {
        var cs = DatabaseEnvironment.ConnectionString()!;
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = cs,
                ["Discovery:Enabled"] = "false",
                ["Crawler:Enabled"] = "false"
            }));
        });
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<(int Published, int Coordinated)> DirectCountsAsync()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        using var scope = root.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        return await db.QueryFirstAsync<(int, int)>(new CommandDefinition(
            """
            SELECT COUNT(*),
                   SUM(CAST((latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND NOT (ABS(latitude) < 0.000001 AND ABS(longitude) < 0.000001)) AS SIGNED))
            FROM restaurants WHERE status='published'
            """));
    }

    [Fact]
    public async Task GeoJsonEndpointMatchesPublishedCoordinatesExactly()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/dados-abertos/geojson");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("FeatureCollection", doc.RootElement.GetProperty("type").GetString());
        var (published, coordinated) = await DirectCountsAsync();
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal(published, metadata.GetProperty("published_total").GetInt32());
        Assert.Equal(coordinated, metadata.GetProperty("with_coordinates").GetInt32());
        Assert.Equal("ODbL-1.0", metadata.GetProperty("license").GetString());
        var features = doc.RootElement.GetProperty("features");
        Assert.Equal(coordinated, features.GetArrayLength());
        if (features.GetArrayLength() > 0)
        {
            var first = features[0];
            Assert.Equal("Point", first.GetProperty("geometry").GetProperty("type").GetString());
            Assert.Equal(2, first.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
            Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("properties").GetProperty("name").GetString()));
        }
    }

    [Fact]
    public async Task CsvEndpointExportsEveryPublishedRowWithStableHeader()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/dados-abertos.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "o CSV deveria começar com BOM UTF-8 para abrir corretamente no Excel pt-BR");
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.True(text.StartsWith("id,name,city,neighborhood,category,address,latitude,longitude,tier,business_status,opening_hours,phone,website\r\n", StringComparison.Ordinal),
            "o cabeçalho CSV deveria ser estável");
        var (published, _) = await DirectCountsAsync();
        var records = CountCsvRecords(text);
        Assert.True(records == published + 1, $"o CSV deveria conter cabeçalho + {published} linhas — contadas {records}");
    }

    [Fact]
    public async Task OpenDataPageShowsHonestCoverageAndKeepsSnapshotRoute()
    {
        using var client = CreateClient();
        var page = await client.GetAsync("/dados-abertos");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Atlas aberto do Pará", html);
        Assert.Contains("ODbL", html);
        Assert.Contains("/api/dados-abertos/geojson", html);
        Assert.Contains("/api/dados-abertos.csv", html);
        var (published, _) = await DirectCountsAsync();
        Assert.Contains(published.ToString(CultureInfo.InvariantCulture), html);

        // A rota pré-existente de snapshots OSM continua viva após a reorganização do controller.
        var snapshot = await client.GetAsync("/dados-abertos/openstreetmap");
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
    }

    private static int CountCsvRecords(string text)
    {
        // Parser mínimo RFC 4180: contagem de registros respeitando quebras dentro de aspas.
        var i = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        var records = 0;
        var inQuotes = false;
        var hasField = false;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (hasField) { records++; hasField = false; }
            }
            else if (!inQuotes && c == '\n')
            {
                if (hasField) { records++; hasField = false; }
            }
            else if (!inQuotes) hasField = true;
        }
        if (hasField) records++;
        return records;
    }
}
