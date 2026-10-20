using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Dapper;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class TransitJourneyTests
{
    [Fact]
    public async Task PublishedPlaceRendersProviderGeometryAndKeepsOriginOutOfShareLinks()
    {
        using var app=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{
            b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:MySql"]=DatabaseEnvironment.ConnectionString(),["GoogleMaps:Enabled"]="true",["GoogleMaps:BrowserKey"]="fixture-browser-key"
            }));
            b.ConfigureServices(s=>{s.RemoveAll<IMobilityProvider>();s.AddSingleton<IMobilityProvider,FixtureMobility>();});
        });
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        using var scope=app.Services.CreateScope();var repo=scope.ServiceProvider.GetRequiredService<IRestaurantReadRepository>();
        var restaurant=(await repo.SearchAsync(new(PageSize:1),default)).Items.First();
        var page=await client.GetStringAsync("/cuia-rotas?restaurantId="+restaurant.Id);
        var token=WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var response=await client.PostAsync("/cuia-rotas/consultar",new FormUrlEncodedContent(new Dictionary<string,string>{
            ["__RequestVerificationToken"]=token,["RestaurantId"]=restaurant.Id.ToString(),["Origin"]="ORIGEM PRIVADA TESTE",["Mode"]="transit",
            ["LocalTime"]=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm"),["MaxWalkMeters"]="1000",["Preference"]="less_walking"
        }));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();
        var decoded=WebUtility.HtmlDecode(html);
        Assert.Contains("TESTE SIMULADO",decoded);Assert.Contains("Sentido fixture",decoded);Assert.Contains("data-route=",html);
        Assert.DoesNotContain("maps.googleapis.com/maps/api/js",html); // progressive loader only after click
        Assert.Contains("maps.googleapis.com",response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store",response.Headers.CacheControl!.ToString());
        var shared=Regex.Match(decoded,"https://wa.me/[^\"]+").Value;Assert.DoesNotContain("PRIVADA",Uri.UnescapeDataString(shared));
        var output=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(output is not null){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"transit-fixture.html"),html);}
        // Home discovery must not depend on Guia being populated.
        var home=WebUtility.HtmlDecode(await client.GetStringAsync("/"));Assert.Contains("fichas para explorar",home);
    }
    private sealed class FixtureMobility:IMobilityProvider
    {
        public string Name=>"Google Routes fixture";
        public bool IsConfigured=>true;
        public Task<RouteResult> PlanAsync(VisitRequest request,RestaurantView restaurant,CancellationToken ct)
        {
            const string encoded="_p~iF~ps|U_ulLnnqC_mqNvxq`@";var path=RouteGeometry.Decode(encoded);
            RouteStep[] steps=[new("WALK","Caminhada fixture",DistanceMeters:100){Path=path},new("TRANSIT","Ônibus fixture","TESTE","Embarque fixture","Desembarque fixture",DistanceMeters:1000){Path=path,Headsign="Sentido fixture",BoardLocation=path[0],AlightLocation=path[^1]}];
            return Task.FromResult(new RouteResult(Name,"available","TESTE SIMULADO: não representa ônibus ou cobertura real.",DateTimeOffset.UtcNow,
                [new("TESTE SIMULADO — Geometria de exemplo",1100,1200,steps,EncodedPolyline:encoded){Path=path}],"https://www.google.com/maps/dir/?api=1"));
        }
    }
}
