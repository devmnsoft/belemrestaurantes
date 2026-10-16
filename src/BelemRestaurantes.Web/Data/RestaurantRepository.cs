using Dapper;
using BelemRestaurantes.Web.Domain;
namespace BelemRestaurantes.Web.Data;
public sealed class RestaurantRepository(DbFactory db) {
 public async Task<IEnumerable<Restaurant>> Search(string? city,string? search,int limit=30){
  await using var c=db.Open();
  return await c.QueryAsync<Restaurant>(@"SELECT id,name,city,neighborhood,category,address,website,instagram,facebook,tiktok,youtube,photo_url AS PhotoUrl,status,updated_at AS UpdatedAt FROM restaurants WHERE status='published' AND (@city IS NULL OR city=@city) AND (@search IS NULL OR name LIKE CONCAT('%',@search,'%') OR category LIKE CONCAT('%',@search,'%')) ORDER BY name LIMIT @limit",new{city=string.IsNullOrWhiteSpace(city)?null:city,search=string.IsNullOrWhiteSpace(search)?null:search,limit=Math.Clamp(limit,1,100)});
 }
 public async Task<Restaurant?> Find(long id){
  await using var c=db.Open();
  return await c.QueryFirstOrDefaultAsync<Restaurant>(@"SELECT id,name,city,neighborhood,category,address,website,instagram,facebook,tiktok,youtube,photo_url AS PhotoUrl,status,updated_at AS UpdatedAt FROM restaurants WHERE id=@id AND status='published'",new{id});
 }
 public async Task AddSuggestion(RestaurantSuggestion s){
  await using var c=db.Open();
  await c.ExecuteAsync("INSERT INTO suggestions(name,city,website,notes) VALUES (@Name,@City,@Website,@Notes)",s);
 }
 public async Task<int> UpsertDiscovered(string name,string city,string? address,string placeId,string sourceUrl){
  await using var c=db.Open();
  return await c.ExecuteAsync(@"INSERT INTO restaurants(name,city,address,status,google_place_id,created_source_url,last_seen_at) VALUES(@name,@city,@address,'pending',@placeId,@sourceUrl,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE last_seen_at=UTC_TIMESTAMP(6)",new{name,city,address,placeId,sourceUrl});
 }
 public async Task LogSync(string source,string city,string state,int count,string? error){
  await using var c=db.Open();
  await c.ExecuteAsync(@"INSERT INTO sync_runs(source,city,state,item_count,error_message) VALUES(@source,@city,@state,@count,@error)",new{source,city,state,count,error=error?.Length>500?error[..500]:error});
 }
}