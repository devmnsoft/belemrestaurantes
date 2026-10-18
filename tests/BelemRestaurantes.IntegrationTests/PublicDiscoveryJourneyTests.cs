using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class PublicDiscoveryJourneyTests
{
    [Fact]
    public async Task PublishedDetailNearbySearchSuggestionsAndOpenDataWorkTogether()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>();
        var moderation = scope.ServiceProvider.GetRequiredService<IModerationRepository>();
        await using var connection = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker = Guid.NewGuid().ToString("N");
        var suggestionName = "Dica de homologação " + marker;
        long id = 0;
        try
        {
            var place = new DiscoveredPlace("fixture-" + marker, "Cuia da Feira · homologação", "Campina", "Belém", "Restaurante", "Endereço de homologação",
                -1.45, -48.49, null, null, null, null, ["restaurant"]) { Phone = "(91) 3333-4444" };
            id = await ingestion.UpsertDiscoveredAsync(place, "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.True(await moderation.ApproveAsync(id, "fixture-public-journey", default));
            var detail = await client.GetAsync("/establishment/" + id);
            var detailHtml = await detail.Content.ReadAsStringAsync();
            Assert.True(detail.StatusCode == HttpStatusCode.OK, detailHtml);
            Assert.Contains("(91) 3333-4444", detailHtml);
            Assert.Contains("/sugerir?restaurantId=" + id, detailHtml);
            await Snapshot("restaurant-sample", detailHtml);
            var search = await client.GetAsync("/busca?lat=-1.45&lng=-48.49&radius=5&sort=nearby");
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            await Snapshot("catalog-sample", await search.Content.ReadAsStringAsync());
            var form = await client.GetStringAsync("/sugerir");
            var token = WebUtility.HtmlDecode(Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.False(string.IsNullOrEmpty(token));
            var missingCsrf = await client.PostAsync("/sugerir", new FormUrlEncodedContent(new Dictionary<string,string> { ["Name"] = suggestionName, ["City"] = "Belém" }));
            Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
            var unsafeLink = await client.PostAsync("/sugerir", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["__RequestVerificationToken"] = token, ["Name"] = suggestionName, ["City"] = "Belém", ["Website"] = "javascript:alert(1)" }));
            Assert.Equal(HttpStatusCode.OK, unsafeLink.StatusCode);
            Assert.Contains("HTTPS", WebUtility.HtmlDecode(await unsafeLink.Content.ReadAsStringAsync()));
            var saved = await client.PostAsync("/sugerir", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["__RequestVerificationToken"] = token, ["Name"] = suggestionName, ["City"] = "Belém", ["Notes"] = "Fixture controlada. Não publicar." }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            Assert.Equal("pending", await connection.ExecuteScalarAsync<string>("SELECT status FROM suggestions WHERE name=@suggestionName", new { suggestionName }));
            var export = await client.GetAsync("/dados-abertos/openstreetmap");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Contains("ODbL-1.0", await export.Content.ReadAsStringAsync());
        }
        finally
        {
            await connection.ExecuteAsync("DELETE FROM suggestions WHERE name=@suggestionName", new { suggestionName });
            if (id > 0) await connection.ExecuteAsync("DELETE FROM restaurants WHERE id=@id", new { id });
        }
    }

    private static async Task Snapshot(string name, string html)
    {
        var output = Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, name + ".html"), html);
        }
    }
}
