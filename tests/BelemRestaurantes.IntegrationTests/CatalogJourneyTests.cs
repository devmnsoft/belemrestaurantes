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
    public async Task Ingestion_counts_commits_preserves_contacts_and_separates_branches()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var ingestion = provider.GetRequiredService<IRestaurantIngestionRepository>();
        var marker = Guid.NewGuid().ToString("N");
        var place = new DiscoveredPlace("fixture-" + marker, "Sabores " + marker, "Nazaré", "Belém", "restaurante",
            "Endereço fixture " + marker, -1.45, -48.49, null, null, null, null, ["restaurant"])
            { Phone = "(91) 3333-4444", Instagram = "https://www.instagram.com/fixture" };
        var ids = new List<long>();
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            var first = await ingestion.UpsertWithOutcomeAsync(place, "test_fixture", DateTimeOffset.UtcNow, default);
            ids.Add(first.Id);
            Assert.True(first.Created);
            var repeat = await ingestion.UpsertWithOutcomeAsync(place, "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.Equal(first.Id, repeat.Id);
            Assert.False(repeat.Created);
            var otherSource = await ingestion.UpsertWithOutcomeAsync(place with { ExternalId = "fixture-other-" + marker }, "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.Equal(first.Id, otherSource.Id);
            Assert.False(otherSource.Created);
            Assert.Equal(place.Phone, await connection.ExecuteScalarAsync<string>("SELECT phone FROM restaurants WHERE id=@id", new { id = first.Id }));
            Assert.Equal(place.Instagram, await connection.ExecuteScalarAsync<string>("SELECT instagram FROM restaurants WHERE id=@id", new { id = first.Id }));
            var branch = await ingestion.UpsertWithOutcomeAsync(place with { ExternalId = "fixture-branch-" + marker, Address = "Outra rua " + marker }, "test_fixture", DateTimeOffset.UtcNow, default);
            ids.Add(branch.Id);
            Assert.True(branch.Created);
            Assert.NotEqual(first.Id, branch.Id);
            await connection.ExecuteAsync("INSERT INTO restaurant_owners(restaurant_id,user_id) VALUES(@id,@user)", new { id = first.Id, user = "fixture-" + marker });
            var owned = await provider.GetRequiredService<IPartnerPortalService>().ListOwnedRestaurantsAsync("fixture-" + marker, default);
            Assert.Equal(first.Id, Assert.Single(owned).Id);
            Assert.Equal(place.Name, owned[0].Name);
            await ingestion.UpsertWithOutcomeAsync(place with { Name = "Nome externo divergente", Phone = "000" }, "test_fixture", DateTimeOffset.UtcNow, default);
            Assert.Equal(place.Name, await connection.ExecuteScalarAsync<string>("SELECT name FROM restaurants WHERE id=@id", new { id = first.Id }));
            Assert.Equal(place.Phone, await connection.ExecuteScalarAsync<string>("SELECT phone FROM restaurants WHERE id=@id", new { id = first.Id }));
        }
        finally
        {
            if (ids.Count > 0)
            {
                await connection.ExecuteAsync("DELETE FROM restaurant_owners WHERE restaurant_id IN @ids", new { ids });
                await connection.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids", new { ids });
            }
        }
    }

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
            var lastPendingPage = Math.Max(1, ((await moderation.CountPendingAsync(default)) + 99) / 100);
            Assert.Contains((await moderation.ListPendingAsync(lastPendingPage, 100, default)).Items, item => item.Id == id);
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

    [Fact]
    public async Task PendingModerationFiltersPreservePagingAndDoNotInterpretSqlOrLikeWildcards()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var ingestion=provider.GetRequiredService<IRestaurantIngestionRepository>();
        var moderation=provider.GetRequiredService<IModerationRepository>();
        var marker=Guid.NewGuid().ToString("N");
        var ids=new List<long>();
        await using var connection=await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            foreach(var item in new[]{("Lanche "+marker,"Lanchonete"),("Feira "+marker,"Feira e mercado")})
            {
                var place=new DiscoveredPlace("fixture-"+Guid.NewGuid().ToString("N"),item.Item1,"Nazaré","Belém",item.Item2,
                    "Rua de teste",-1.45,-48.49,null,null,null,null,["restaurant"]) { Phone="(91) 3333-4444" };
                ids.Add(await ingestion.UpsertDiscoveredAsync(place,"test_fixture",DateTimeOffset.UtcNow,default));
            }
            var results=await moderation.ListPendingAsync(1,1,default,marker,"Belém");
            Assert.Equal(2,results.TotalCount);Assert.Single(results.Items);Assert.Equal(2,results.TotalPages);
            Assert.Equal(ids[1],Assert.Single((await moderation.ListPendingAsync(2,1,default,marker,"Belém")).Items).Id);
            var feira=Assert.Single((await moderation.ListPendingAsync(1,30,default,marker,"Belém","feira")).Items);
            Assert.Equal("Feira e mercado",feira.Category);Assert.Equal("Rua de teste",feira.Address);Assert.Equal("(91) 3333-4444",feira.Phone);
            Assert.Empty((await moderation.ListPendingAsync(1,30,default,marker,"Ananindeua")).Items);
            Assert.Empty((await moderation.ListPendingAsync(1,30,default,"%' OR 1=1 --")).Items);
            Assert.Empty((await moderation.ListPendingAsync(1,30,default,marker+"%")).Items);
        }
        finally { if(ids.Count>0) await connection.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids",new{ids}); }
    }

    [Fact]
    public async Task TerritorialCountsExcludePendingImportsUntilModerationPublishesThem()
    {
        using var provider=(ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var catalog=provider.GetRequiredService<IRestaurantReadRepository>();
        var before=(await catalog.CountPublishedByCityAsync(default)).GetValueOrDefault("belem");
        var marker=Guid.NewGuid().ToString("N");
        var place=new DiscoveredPlace("fixture-atlas-"+marker,"Atlas "+marker,null,"Belém","Restaurante",null,null,null,null,null,null,null,["restaurant"]);
        var id=await provider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(place,"test_fixture",DateTimeOffset.UtcNow,default);
        await using var connection=await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            Assert.Equal(before,(await catalog.CountPublishedByCityAsync(default)).GetValueOrDefault("belem"));
            Assert.True(await provider.GetRequiredService<IModerationRepository>().ApproveAsync(id,"fixture-atlas",default));
            Assert.Equal(before+1,(await catalog.CountPublishedByCityAsync(default)).GetValueOrDefault("belem"));
        }
        finally { await connection.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id}); }
    }
}
