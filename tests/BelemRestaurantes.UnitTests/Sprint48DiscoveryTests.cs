using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace BelemRestaurantes.UnitTests;
public sealed class Sprint48DiscoveryTests
{
    [Fact]
    public void SnapshotPagesAdvanceWithoutRepeatingAndRestartWhenSourceChanges()
    {
        var at=DateTimeOffset.UtcNow;
        var places=Enumerable.Range(0,125).Select(i=>new DiscoveredPlace("osm:"+i.ToString("D3"),"Lugar "+i,null,"Belém","Restaurante","Rua de teste",null,null,null,null,null,null,[])).Reverse().ToArray();
        var source=new PlaceSearchResult(places,null,at);
        var first=SnapshotDiscoveryPage.Slice(source,50,0,null);var second=SnapshotDiscoveryPage.Slice(source,50,first.Progress.NextOffset,at);
        var last=SnapshotDiscoveryPage.Slice(source,50,second.Progress.NextOffset,at);
        Assert.Equal(50,first.Result.Places.Count);Assert.Equal(25,last.Result.Places.Count);Assert.False(last.Progress.HasMore);
        Assert.Equal(125,first.Result.Places.Concat(second.Result.Places).Concat(last.Result.Places).Select(p=>p.ExternalId).Distinct().Count());
        var refreshed=SnapshotDiscoveryPage.Slice(source with{QueriedAt=at.AddHours(24)},50,100,at);
        Assert.Equal(first.Result.Places.Select(p=>p.ExternalId),refreshed.Result.Places.Select(p=>p.ExternalId));
    }
    [Fact]
    public void StructuredMenusHoursAndVideoAreExtractedWithoutGuessingDishesFromCuisine()
    {
        const string html="""<script type="application/ld+json">{"@type":"Restaurant","name":"Casa de Teste","servesCuisine":"Paraense","hasMenu":{"@type":"Menu","hasMenuSection":{"@type":"MenuSection","hasMenuItem":[{"@type":"MenuItem","name":"Prato documentado"}]}},"openingHours":"Mo 10:00-14:00","video":{"@type":"VideoObject","contentUrl":"https://www.youtube.com/watch?v=fixture"}}</script>""";
        var row=Assert.Single(new CrawlerParser(NullLogger<CrawlerParser>.Instance).ExtractRestaurants(html,"https://example.org/menu","Belém"));
        Assert.Equal("Prato documentado",Assert.Single(row.MenuItems));Assert.Single(row.OpeningHours);Assert.Contains("youtube",row.YouTube!);
        Assert.DoesNotContain("Tacacá",row.MenuItems);
    }
}
