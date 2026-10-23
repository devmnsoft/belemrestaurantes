using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
namespace BelemRestaurantes.Infrastructure.Crawler;
public sealed class MySqlExternalContent(MySqlConnectionFactory factory,ICrawlerParser parser):IExternalContent
{
    private const string Select="SELECT id Id,title Title,url Url,publisher Publisher,city City,summary Summary,COALESCE(facts_json,'{}') FactsJson,kind Kind,video_url VideoUrl,published_at PublishedAt,observed_at ObservedAt,state State FROM external_content ";
    public async Task<IReadOnlySet<string>> SuppressedUrlsAsync(IEnumerable<string> urls,CancellationToken ct)
    {
        var hashes=urls.Select(ExternalContentRules.CanonicalUrl).Where(u=>u is not null).Distinct().Take(20).Select(u=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(u!)))).ToArray();
        if(hashes.Length==0)return new HashSet<string>(StringComparer.Ordinal);
        await using var db=await factory.OpenAsync(ct);return (await db.QueryAsync<string>(new CommandDefinition("SELECT url FROM external_content WHERE state='suspended' AND url_hash IN @hashes",new{hashes},cancellationToken:ct))).ToHashSet(StringComparer.Ordinal);
    }
    public async Task<long?> ObserveAsync(ExternalContent item,CancellationToken ct)
    {
        var url=ExternalContentRules.CanonicalUrl(item.Url);
        var title=ExternalContentRules.Clean(item.Title,220);
        if(url is null||title.Length<5||!ExternalContentRules.Relevant(title+" "+item.Summary,item.City))return null;
        var publisher=new Uri(url).Host;
        var video=!string.IsNullOrWhiteSpace(item.VideoUrl)?ExternalContentRules.CanonicalUrl(item.VideoUrl):ExternalContentRules.Video(url)?url:null;
        var f=item.Facts;
        var facts=JsonSerializer.Serialize(new ExternalContentFacts(f.PlaceName is null?null:ExternalContentRules.Clean(f.PlaceName,160),f.Address is null?null:ExternalContentRules.Clean(f.Address,300),(f.MenuItems??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>ExternalContentRules.Clean(x,180)).Distinct().Take(12).ToArray(),(f.OpeningHours??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>ExternalContentRules.Clean(x,180)).Distinct().Take(14).ToArray(),string.IsNullOrWhiteSpace(f.Description)?null:ExternalContentRules.Clean(f.Description,500)));
        var hasFacts=!string.IsNullOrWhiteSpace(f.Description)||!string.IsNullOrWhiteSpace(f.PlaceName)||!string.IsNullOrWhiteSpace(f.Address)||(f.MenuItems?.Count??0)>0||(f.OpeningHours?.Count??0)>0;
        var summary=ExternalContentRules.Summary(item.City,publisher,video is not null);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO external_content(title,url,url_hash,publisher,city,summary,kind,video_url,published_at,observed_at,facts_json)
            VALUES(@title,@url,@hash,@publisher,@city,@summary,@kind,@video,@published,@observed,@facts)
            ON DUPLICATE KEY UPDATE title=VALUES(title),facts_json=CASE WHEN @hasFacts THEN VALUES(facts_json) ELSE COALESCE(facts_json,VALUES(facts_json)) END,summary=VALUES(summary),video_url=COALESCE(VALUES(video_url),video_url),published_at=COALESCE(VALUES(published_at),published_at),observed_at=GREATEST(observed_at,VALUES(observed_at)),updated_at=UTC_TIMESTAMP(6)
            """,new{title,url,hash,publisher,facts,hasFacts,city=item.City,summary,kind=video is not null?"video":"article",video,published=item.PublishedAt,observed=item.ObservedAt==default?DateTime.UtcNow:item.ObservedAt},tx,cancellationToken:ct));
        var id=await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT id FROM external_content WHERE url_hash=@hash",new{hash},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES('system:content-collector','external_content.observe','external_content',@id,@metadata,UTC_TIMESTAMP(6))",new{id,metadata=JsonSerializer.Serialize(new{url,title,kind=video is null?"article":"video"})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return id;
    }
    public async Task ObservePageAsync(string html,string url,string city,CancellationToken ct)
    {
        if(html.Length>2*1024*1024)return;
        var title=Meta(html,"og:title");
        if(title.Length==0){var m=Regex.Match(html,@"<title[^>]*>(.*?)</title>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100));title=m.Success?ExternalContentRules.Clean(m.Groups[1].Value,220):"";}
        var type=Meta(html,"og:type");
        // Directory landing pages are not articles. A title alone never publishes the whole page.
        if(type!="article"&&!type.StartsWith("video",StringComparison.OrdinalIgnoreCase)&&!Regex.IsMatch(html,""""@type"\s*:\s*"(NewsArticle|Article|BlogPosting|VideoObject)"""",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)))return;
        var date=Meta(html,"article:published_time");
        var video=Meta(html,"og:video:secure_url");if(video.Length==0)video=Meta(html,"og:video:url");if(video.Length==0)video=Meta(html,"og:video");if(video.Length==0)video=SchemaVideo(html)??"";
        var places=parser.ExtractRestaurants(html,url,city).Where(p=>p.StructuredIdentity&&p.ExplicitCity).Take(2).ToArray();
        var place=places.Length==1?places[0]:null;
        var facts=JsonSerializer.Serialize(new ExternalContentFacts(place?.Name,place?.Address,place?.MenuItems,place?.OpeningHours));
        await ObserveAsync(new(){FactsJson=facts,Title=title,Url=url,City=place?.City??city,Summary=Meta(html,"og:description"),VideoUrl=video,PublishedAt=DateTimeOffset.TryParse(date,out var published)?published.UtcDateTime:null,ObservedAt=DateTime.UtcNow},ct);
    }
    public async Task<IReadOnlyList<ExternalContent>> ListAsync(string? query,string? city,int limit,CancellationToken ct,bool includeSuspended=false)
    {
        await using var db=await factory.OpenAsync(ct);
        var noise=new[]{"onde","comer","restaurante","restaurantes","pratos","prato","cardapio","cardápio","diga","sobre","quais","como","chego","preços","precos","belém","belem","para","pará","contexto","pesquise","pesquisa","posso","confirmar","atual","atualizado","preço","preco","horário","horario","funcionamento","turismo","gastronomia","significa","explique","seria","fale","conhecer"};
        var terms=(query??"").Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim('?', '.', ',',':')).Where(t=>t.Length>=4&&!noise.Contains(t.ToLowerInvariant())).Distinct().Take(5).ToArray();
        if(!string.IsNullOrWhiteSpace(query)&&terms.Length==0)return [];
        var p=new DynamicParameters(new{includeSuspended,city=string.IsNullOrWhiteSpace(city)?null:city,limit=Math.Clamp(limit,1,60)});
        var filters=new List<string>();
        for(var i=0;i<terms.Length;i++){p.Add("q"+i,"%"+MySqlRestaurantReadRepository.EscapeLike(terms[i])+"%");filters.Add("(title LIKE @q"+i+" OR summary LIKE @q"+i+" OR facts_json LIKE @q"+i+")");}
        return (await db.QueryAsync<ExternalContent>(new CommandDefinition(Select+"WHERE (@includeSuspended=1 OR state='published') AND (@city IS NULL OR city=@city)"+(filters.Count==0?"":" AND ("+string.Join(" OR ",filters)+")")+" ORDER BY observed_at DESC,id DESC LIMIT @limit",p,cancellationToken:ct))).ToArray();
    }
    public async Task<ExternalContent?> GetAsync(long id,CancellationToken ct){await using var db=await factory.OpenAsync(ct);return await db.QuerySingleOrDefaultAsync<ExternalContent>(new CommandDefinition(Select+"WHERE id=@id AND state='published'",new{id},cancellationToken:ct));}
    public async Task<bool> SetStateAsync(long id,string state,string actor,CancellationToken ct)
    {
        if(state is not ("published" or "suspended"))return false;
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var n=await db.ExecuteAsync(new CommandDefinition("UPDATE external_content SET state=@state,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND state<>@state",new{id,state},tx,cancellationToken:ct));
        if(n>0)await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'external_content.state','external_content',@id,@metadata,UTC_TIMESTAMP(6))",new{actor,id,metadata=JsonSerializer.Serialize(new{state})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return n>0;
    }
    private static string? SchemaVideo(string html)
    {
        foreach(Match script in Regex.Matches(html,"""<script[^>]*type=["']application/ld\+json["'][^>]*>(.*?)</script>""",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100)).Cast<Match>().Take(12))
        {
            try{using var json=JsonDocument.Parse(script.Groups[1].Value);var found=Find(json.RootElement,0);if(found is not null)return found;}catch(JsonException){}
        }
        return null;
        static string? Find(JsonElement e,int depth)
        {
            if(depth>8)return null;
            if(e.ValueKind==JsonValueKind.Object)
            {
                if(e.TryGetProperty("@type",out var type)&&type.ToString().Contains("VideoObject",StringComparison.Ordinal))
                    foreach(var key in new[]{"embedUrl","contentUrl","url"})if(e.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String&&ExternalContentRules.CanonicalUrl(value.GetString()??"") is {} url)return url;
                foreach(var p in e.EnumerateObject().Take(40)){var found=Find(p.Value,depth+1);if(found is not null)return found;}
            }
            if(e.ValueKind==JsonValueKind.Array)foreach(var child in e.EnumerateArray().Take(40)){var found=Find(child,depth+1);if(found is not null)return found;}
            return null;
        }
    }
    private static string Meta(string html,string key)
    {
        foreach(Match tag in Regex.Matches(html,@"<meta\b[^>]{0,4000}>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)))
        {
            var attributes=Regex.Matches(tag.Value,"""([a-zA-Z:_-]+)\s*=\s*["']([^"']*)["']""",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Cast<Match>().GroupBy(m=>m.Groups[1].Value.ToLowerInvariant()).ToDictionary(g=>g.Key,g=>g.First().Groups[2].Value);
            if((attributes.GetValueOrDefault("property")??attributes.GetValueOrDefault("name"))==key&&attributes.TryGetValue("content",out var value))return WebUtility.HtmlDecode(value);
        }
        return "";
    }
}
