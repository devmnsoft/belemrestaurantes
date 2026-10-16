using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class CatalogJourneyTests
{
    [Fact]
    public async Task Fixture_place_is_deduplicated_moderated_published_and_audited_in_mysql()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var ingestion = provider.GetRequiredService<IRestaurantIngestionRepository>();
        var moderation = provider.GetRequiredService<IModerationRepository>();
        var catalog = provider.GetRequiredService<IRestaurantReadRepository>();
        var marker = Guid.NewGuid().ToString("N");
        var place = new DiscoveredPlace("fixture-" + marker, "Teste " + marker, "Nazaré", "Belém", "restaurante",
            "Endereço de teste", -1.45, -48.49, "https://maps.google.com/", null, null, null, ["restaurant"]);
        long id = 0;
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            id = await ingestion.UpsertDiscoveredAsync(place, "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.Equal(id, await ingestion.UpsertDiscoveredAsync(place, "test_fixture", DateTimeOffset.UtcNow, default));
            await ingestion.RecordObservationAsync(id, "test_fixture", place.ExternalId, null, "fixture-hash", DateTimeOffset.UtcNow, default);
            Assert.Null(await catalog.GetPublishedAsync(id, default));
            Assert.Contains((await moderation.ListPendingAsync(1, 100, default)).Items, item => item.Id == id);
            Assert.True(await moderation.ApproveAsync(id, "test-moderator", default));
            var published = await catalog.GetPublishedAsync(id, default);
            Assert.NotNull(published);
            Assert.Equal(place.Name, published.Name);
            Assert.Single((await catalog.SearchAsync(new RestaurantSearchQuery(Term: marker), default)).Items);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE entity_id=@id AND action='restaurant.approve'", new { id }));
        }
        finally
        {
            if (id > 0)
            {
                await connection.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id=@id", new { id });
                await connection.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id=@id", new { id });
                await connection.ExecuteAsync("DELETE FROM restaurants WHERE id=@id", new { id });
            }
        }
    }
}
