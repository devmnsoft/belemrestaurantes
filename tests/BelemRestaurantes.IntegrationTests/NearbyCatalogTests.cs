using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class NearbyCatalogTests
{
    [Fact]
    public async Task NearbyFiltersPublishedPlacesByRadius_AndPreservesPhotoAndSortFilters()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        var ingestion = provider.GetRequiredService<IRestaurantIngestionRepository>();
        var moderation = provider.GetRequiredService<IModerationRepository>();
        var catalog = provider.GetRequiredService<IRestaurantReadRepository>();
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker = Guid.NewGuid().ToString("N");
        var ids = new List<long>();
        try
        {
            foreach (var (name, latitude) in new[] { ("Perto", -1.45), ("Longe", -1.70) })
            {
                var place = new DiscoveredPlace("fixture-" + name + marker, name + " " + marker, "Centro", "Belém", "Restaurante", "Rua fixture " + name,
                    latitude, -48.49, "https://example.org/fixture", null, null, null, ["restaurant"]);
                var id = await ingestion.UpsertDiscoveredAsync(place, "test_fixture", DateTimeOffset.UtcNow, default);
                ids.Add(id);
                Assert.True(await moderation.ApproveAsync(id, "fixture-nearby", default));
            }
            var query = new RestaurantSearchQuery(Term: marker, Latitude: -1.45, Longitude: -48.49, RadiusKm: 5, Sort: "nearby");
            var near = Assert.Single((await catalog.SearchAsync(query, default)).Items);
            Assert.Equal(ids[0], near.Id);
            Assert.InRange(near.DistanceKm!.Value, 0, 0.01);
            Assert.Empty((await catalog.SearchAsync(query with { PhotosOnly = true }, default)).Items);
            await connection.ExecuteAsync("UPDATE restaurants SET photo_url='/media/photo/fixture.jpg' WHERE id=@id", new { id = ids[0] });
            Assert.Single((await catalog.SearchAsync(query with { PhotosOnly = true }, default)).Items);
            var expanded = await catalog.SearchAsync(query with { RadiusKm = 50 }, default);
            Assert.Equal(2, expanded.TotalCount);
            Assert.Equal(ids[0], expanded.Items[0].Id);
            Assert.Equal(2, (await catalog.SearchAsync(new RestaurantSearchQuery(Term: marker, Sort: "name; DROP TABLE restaurants"), default)).TotalCount);
        }
        finally
        {
            if (ids.Count > 0) await connection.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids", new { ids });
        }
    }
}
