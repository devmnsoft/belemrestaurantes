using System.Diagnostics;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class MapScaleTests
{
    [Fact]
    public async Task BoundedViewportAtFourCatalogueSizes()
    {
        // Opt in because this writes 61,100 temporary synthetic records, cleaned in finally.
        if (Environment.GetEnvironmentVariable("MAP_SCALE_TEST") != "1") return;
        using var provider=(ServiceProvider)ServiceContainer.Build();
        var repository=provider.GetRequiredService<IRestaurantReadRepository>();
        await using var db=await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        Assert.Contains(db.DataSource,new[]{"localhost","127.0.0.1"});
        var marker="mapscale"+Guid.NewGuid().ToString("N");var results=new List<object>();
        await db.ExecuteAsync("CREATE TEMPORARY TABLE map_digits(n INT PRIMARY KEY)");
        await db.ExecuteAsync("INSERT INTO map_digits VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)");
        try {
            foreach(var count in new[]{100,1000,10000,50000}) {
                await db.ExecuteAsync("""
                    INSERT INTO restaurants(name,normalized_name,city,normalized_city,latitude,longitude,status,category)
                    SELECT CONCAT(@marker,a.n+10*b.n+100*c.n+1000*d.n+10000*e.n),@marker,'Santarém','santarem',-2.43,-54.70,'published','Tacacá'
                    FROM map_digits a CROSS JOIN map_digits b CROSS JOIN map_digits c CROSS JOIN map_digits d CROSS JOIN map_digits e
                    LIMIT @count
                    """,new{marker,count},commandTimeout:120);
                var times=new List<double>();var legacyTimes=new List<double>();var watch=new Stopwatch();
                for(var sample=0;sample<30;sample++) {
                    watch.Restart();var page=await repository.SearchMapAsync(new MapQuery("Santarém",marker,Bounds:new(-55,-3,-54,-2),Limit:200),default);times.Add(watch.Elapsed.TotalMilliseconds);
                    Assert.Equal(Math.Min(count,200),page.Points.Count);
                    watch.Restart();_ = (await db.QueryAsync<CatalogMapPoint>("SELECT id,name,city,neighborhood,category,latitude,longitude,catalog_tier FROM restaurants WHERE status='published' AND latitude IS NOT NULL AND longitude IS NOT NULL ORDER BY city,name,id LIMIT 500")).AsList();legacyTimes.Add(watch.Elapsed.TotalMilliseconds);
                }
                times.Sort();legacyTimes.Sort();
                var explain=(await db.QueryAsync("EXPLAIN SELECT id,name,latitude,longitude FROM restaurants WHERE status='published' AND normalized_city='santarem' AND latitude BETWEEN -3 AND -2 AND longitude BETWEEN -55 AND -54 AND id>0 ORDER BY id LIMIT 201")).Select(r=>(IDictionary<string,object>)r).ToList();
                results.Add(new{count,samples=times.Count,medianMs=times[15],p95Ms=times[28],legacyMedianMs=legacyTimes[15],legacyP95Ms=legacyTimes[28],explain});
                await db.ExecuteAsync("DELETE FROM restaurants WHERE normalized_name=@marker",new{marker},commandTimeout:120);
            }
        } finally { await db.ExecuteAsync("DELETE FROM restaurants WHERE normalized_name=@marker",new{marker},commandTimeout:120); }
        var folder=Environment.GetEnvironmentVariable("MAP_SCALE_OUTPUT")??Path.Combine(Path.GetTempPath(),"belem-map-scale");Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder,"scale.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
