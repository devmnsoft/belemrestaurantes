using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class MapRepositoryTests
{
    [Fact]
    public async Task FiltersBeforeLimit_PaginatesAndExcludesPendingAndInvalidCoordinates()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        var repository = provider.GetRequiredService<IRestaurantReadRepository>();
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        // Synthetic data must never be inserted into a remote catalogue.
        Assert.Contains(connection.DataSource, new[] { "localhost", "127.0.0.1" });
        var marker = "maptest" + Guid.NewGuid().ToString("N");
        try
        {
            const string sql = "INSERT INTO restaurants(name,normalized_name,city,normalized_city,category,neighborhood,neighborhood_normalized,latitude,longitude,status) VALUES(@name,@name,'Belém','belem','Tacacá','Nazaré','nazare',@lat,-48.49,@status)";
            var fixtures = Enumerable.Range(0, 505).Select(i => new { name=marker+i, lat=-1.45, status="published" });
            await connection.ExecuteAsync(sql, fixtures);
            await connection.ExecuteAsync(sql, new { name=marker+"pending", lat=-1.45, status="pending" });
            await connection.ExecuteAsync(sql, new { name=marker+"invalid", lat=95.0, status="published" });
            var query = new MapQuery("Belém", marker, "Tacacá", "Nazaré", "ficha", new(-49,-2,-48,0), Limit:200);
            var ids = new HashSet<long>(); var pageCount=0;
            do {
                var page = await repository.SearchMapAsync(query, default);
                Assert.InRange(page.Points.Count,1,200);
                Assert.All(page.Points,p=> { Assert.True(ids.Add(p.Id)); Assert.True(CatalogMapLayout.IsPlottable(p.Latitude,p.Longitude)); });
                pageCount++;
                if(page.Next is not {} next)break;
                query=query with { After=next };
            } while(pageCount<5);
            Assert.Equal(505,ids.Count); Assert.Equal(3,pageCount);
            Assert.Single((await repository.SearchMapAsync(new MapQuery(Term:marker+"504"),default)).Points);
            Assert.Empty((await repository.SearchMapAsync(new MapQuery(Term:marker, Bounds:new(-40,-2,-39,0)),default)).Points);
            Assert.Empty((await repository.SearchMapAsync(new MapQuery(Term:"' OR 1=1 --"),default)).Points);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>repository.SearchMapAsync(query,cancelled.Token));
        }
        finally { await connection.ExecuteAsync("DELETE FROM restaurants WHERE name LIKE @term",new {term=marker+"%"}); }
    }
}
