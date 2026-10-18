using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;
public sealed class MySqlParaRecipes(MySqlConnectionFactory factory):IParaRecipes
{
    private sealed class Row {public long Id{get;set;}public string Body{get;set;}="";public string Status{get;set;}="";public int Version{get;set;}public DateTime UpdatedAt{get;set;}}
    private static ParaRecipe Read(Row row){var r=JsonSerializer.Deserialize<ParaRecipe>(row.Body)!;r.Id=row.Id;r.Status=row.Status;r.Version=row.Version;r.UpdatedAt=row.UpdatedAt;return r;}
    public async Task<PagedResult<ParaRecipe>> ListAsync(RecipeFilter f,bool admin,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        const string where="""
          FROM para_recipes p WHERE (@admin OR p.status='published')
          AND (@term IS NULL OR p.search_text LIKE @term OR EXISTS(
            SELECT 1 FROM para_recipe_restaurants match_recipe JOIN restaurants match_restaurant ON match_restaurant.id=match_recipe.restaurant_id
            WHERE match_recipe.recipe_id=p.id AND match_restaurant.status='published' AND match_restaurant.name LIKE @term))
          AND (@category IS NULL OR p.category=@category) AND (@course IS NULL OR p.course=@course)
          AND (@city IS NULL OR p.city=@city) AND (NOT @quick OR p.total_minutes<=30)
          AND (@restaurant IS NULL OR EXISTS(SELECT 1 FROM para_recipe_restaurants pr JOIN restaurants r ON r.id=pr.restaurant_id
               WHERE pr.recipe_id=p.id AND r.status='published' AND r.id=@restaurant))
          """;
        var parameters=new DynamicParameters(new{admin,term=string.IsNullOrWhiteSpace(f.Query)?null:"%"+MySqlRestaurantReadRepository.EscapeLike(f.Query.Trim()[..Math.Min(f.Query.Trim().Length,120)])+"%",
            category=string.IsNullOrWhiteSpace(f.Category)?null:f.Category,course=string.IsNullOrWhiteSpace(f.Course)?null:f.Course,city=string.IsNullOrWhiteSpace(f.City)?null:f.City,quick=f.Quick,restaurant=f.RestaurantId});
        var total=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) "+where,parameters,cancellationToken:ct));
        var page=Math.Clamp(f.Page,1,Math.Max(1,(total+11)/12));parameters.Add("offset",(page-1)*12);
        var rows=await db.QueryAsync<Row>(new CommandDefinition("SELECT p.id,p.body,p.status,p.version,p.updated_at "+where+" ORDER BY p.updated_at DESC,p.id DESC LIMIT 12 OFFSET @offset",parameters,cancellationToken:ct));
        return new(rows.Select(Read).ToArray(),page,12,total);
    }
    public async Task<ParaRecipe?> GetAsync(string slug,bool admin,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var row=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("SELECT id,body,status,version,updated_at FROM para_recipes WHERE slug=@slug AND (@admin OR status='published')",new{slug,admin},cancellationToken:ct));
        if(row is null)return null;
        var r=Read(row);r.RestaurantIds=(await db.QueryAsync<long>(new CommandDefinition("SELECT pr.restaurant_id FROM para_recipe_restaurants pr JOIN restaurants r ON r.id=pr.restaurant_id WHERE pr.recipe_id=@id AND r.status='published' ORDER BY pr.restaurant_id",new{id=r.Id},cancellationToken:ct))).ToArray();return r;
    }
    public async Task<long> SaveAsync(ParaRecipe r,string actor,CancellationToken ct)
    {
        RecipeRules.Validate(r);
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(r.RestaurantIds.Length>0 && await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurants WHERE id IN @ids AND status='published'",new{ids=r.RestaurantIds.Distinct().ToArray()},tx,cancellationToken:ct))!=r.RestaurantIds.Distinct().Count())throw new ArgumentException("Um estabelecimento relacionado não está publicado.");
        if(r.Id>0)
        {
            var current=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("SELECT id,body,status,version,updated_at FROM para_recipes WHERE id=@id FOR UPDATE",new{id=r.Id},tx,cancellationToken:ct));
            if(current is null||current.Version!=r.Version)throw new ArgumentException("Esta receita mudou. Reabra a edição para preservar a versão mais recente.");
        }
        r.Version++;r.UpdatedAt=DateTime.UtcNow;
        var body=JsonSerializer.Serialize(r);var search=string.Join(' ',new[]{r.Title,r.Subtitle,r.Story,r.Category,r.City}.Concat(r.Ingredients));
        var p=new{r.Id,r.Slug,r.Title,r.Category,r.Course,r.City,total=r.TotalMinutes,r.Status,r.Version,body,search,actor};
        if(r.Id==0)r.Id=await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO para_recipes(slug,title,category,course,city,total_minutes,status,version,body,search_text,updated_at,updated_by)
            VALUES(@Slug,@Title,@Category,@Course,@City,@total,@Status,@Version,@body,@search,UTC_TIMESTAMP(6),@actor);SELECT LAST_INSERT_ID();
            """,p,tx,cancellationToken:ct));
        else await db.ExecuteAsync(new CommandDefinition("""
            UPDATE para_recipes SET slug=@Slug,title=@Title,category=@Category,course=@Course,city=@City,total_minutes=@total,
            status=@Status,version=@Version,body=@body,search_text=@search,updated_at=UTC_TIMESTAMP(6),updated_by=@actor WHERE id=@Id
            """,p,tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM para_recipe_restaurants WHERE recipe_id=@id",new{id=r.Id},tx,cancellationToken:ct));
        foreach(var restaurant in r.RestaurantIds.Distinct())await db.ExecuteAsync(new CommandDefinition("INSERT INTO para_recipe_restaurants(recipe_id,restaurant_id) VALUES(@id,@restaurant)",new{id=r.Id,restaurant},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO para_recipe_versions(recipe_id,version,body,actor,created_at) VALUES(@id,@version,@body,@actor,UTC_TIMESTAMP(6))",new{id=r.Id,version=r.Version,body,actor},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'recipe.save','recipe',@id,@metadata,UTC_TIMESTAMP(6))",new{actor,id=r.Id,metadata=JsonSerializer.Serialize(new{r.Slug,r.Version,r.Status})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return r.Id;
    }
    public async Task<IReadOnlyList<ParaRecipe>> ForRestaurantAsync(long restaurantId,CancellationToken ct)
    {await using var db=await factory.OpenAsync(ct);var rows=await db.QueryAsync<Row>(new CommandDefinition("""
        SELECT p.id,p.body,p.status,p.version,p.updated_at FROM para_recipes p
        JOIN para_recipe_restaurants pr ON pr.recipe_id=p.id JOIN restaurants r ON r.id=pr.restaurant_id
        WHERE r.id=@restaurantId AND r.status='published' AND p.status='published' ORDER BY p.updated_at DESC LIMIT 3
        """,new{restaurantId},cancellationToken:ct));return rows.Select(Read).ToArray();}
}
