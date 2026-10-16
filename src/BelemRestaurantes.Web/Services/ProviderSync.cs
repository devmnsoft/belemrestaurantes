using System.Net.Http.Json;
using System.Text.Json;
using BelemRestaurantes.Web.Data;
namespace BelemRestaurantes.Web.Services;
public sealed class ProviderSync(IHttpClientFactory factory,IConfiguration config,RestaurantRepository repo,ILogger<ProviderSync> log) {
 public async Task DiscoverTarget(string city,string category,CancellationToken ct) {
  var apiKey=config["GOOGLE_PLACES_API_KEY"];
  if(string.IsNullOrWhiteSpace(apiKey))throw new InvalidOperationException("Google Places API key not configured");
  using var req=new HttpRequestMessage(HttpMethod.Post,"https://places.googleapis.com/v1/places:searchText");
  req.Headers.Add("X-Goog-Api-Key",apiKey);
  req.Headers.Add("X-Goog-FieldMask","places.id,places.displayName");
  req.Content=JsonContent.Create(new{textQuery=$"{category} em {city}, Pará, Brasil",pageSize=20,languageCode="pt-BR"});
  using var resp=await factory.CreateClient("googleplaces").SendAsync(req,ct);
  if(!resp.IsSuccessStatusCode){log.LogWarning("Places search failed: {Status} for {City}",(int)resp.StatusCode,city);throw new HttpRequestException($"Places status {(int)resp.StatusCode}");}
  using var doc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
  var count=0;
  if(doc.RootElement.TryGetProperty("places",out var places)){
   foreach(var item in places.EnumerateArray()){
    if(!item.TryGetProperty("id",out var idEl)||!item.TryGetProperty("displayName",out var display)||!display.TryGetProperty("text",out var nameEl))continue;
    var id=idEl.GetString();var name=nameEl.GetString();
    if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(name))continue;
    await repo.UpsertDiscovered(name,city,null,id,"https://www.google.com/maps/place/?q=place_id:"+Uri.EscapeDataString(id));
    count++;
   }
  }
  await repo.LogSync("google_places",city,"completed",count,null);
 }
}