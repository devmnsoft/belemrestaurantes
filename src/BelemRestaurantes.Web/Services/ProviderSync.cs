using System.Net.Http.Json;
using System.Text.Json;
using BelemRestaurantes.Web.Data;
namespace BelemRestaurantes.Web.Services;
public sealed class ProviderSync(IHttpClientFactory httpFactory,IConfiguration config,IServiceScopeFactory scopeFactory,ILogger<ProviderSync> log) {
 public async Task DiscoverCity(string city,CancellationToken cancellationToken) {
  var key=config["GOOGLE_PLACES_API_KEY"];
  if(string.IsNullOrWhiteSpace(key))return;
  using var client=httpFactory.CreateClient("googleplaces");
  foreach(var category in new[]{"restaurantes","pizzarias","açaí","lanchonetes","cafeterias"}) {
   using var request=new HttpRequestMessage(HttpMethod.Post,"https://places.googleapis.com/v1/places:searchText");
   request.Headers.Add("X-Goog-Api-Key",key);
   request.Headers.Add("X-Goog-FieldMask","places.id,places.displayName,places.formattedAddress,nextPageToken");
   request.Content=JsonContent.Create(new {textQuery=$"{category} em {city}, Pará, Brasil",pageSize=20,languageCode="pt-BR"});
   try {
    using var response=await client.SendAsync(request,cancellationToken);
    if(!response.IsSuccessStatusCode){log.LogWarning("Places HTTP {Code} on {City}",response.StatusCode,city);continue;}
    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    if(!json.RootElement.TryGetProperty("places",out var places))continue;
    using var scope=scopeFactory.CreateScope();
    var repo=scope.ServiceProvider.GetRequiredService<RestaurantRepository>();
    var count=0;
    foreach(var place in places.EnumerateArray()) {
     var id=place.GetProperty("id").GetString();
     var name=place.GetProperty("displayName").GetProperty("text").GetString();
     if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(name))continue;
     await repo.UpsertDiscovered(name,city,null,id,"https://www.google.com/maps/place/?q=place_id:"+Uri.EscapeDataString(id));
     count++;
    }
    await repo.LogSync("google_places",city,"completed",count,null);
   } catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
   catch(Exception ex){log.LogError(ex,"Sync error in {City}",city);}
  }
 }
}