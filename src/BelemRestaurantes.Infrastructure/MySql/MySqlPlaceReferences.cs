using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Ai;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlPlaceReferences(MySqlConnectionFactory factory) : IPlaceReferences
{
    public async Task<IReadOnlyList<PlaceReference>> FindAsync(string question,string? city,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var rows=await db.QueryAsync<Row>(new CommandDefinition("""
            SELECT * FROM place_reference_index WHERE status='approved' AND expires_at>UTC_TIMESTAMP(6)
            AND @question LIKE CONCAT('% ',normalized_name,' %')
            AND (@city IS NULL OR city=@city)
            ORDER BY LENGTH(normalized_name) DESC,observed_at DESC LIMIT 8
            """,new{question=" "+NameNormalizer.Normalize(question)+" ",city},cancellationToken:ct));
        return rows.Select(Map).ToArray();
    }

    public async Task<long> ObserveAsync(PlaceReference reference,CancellationToken ct)
    {
        var canonicalCity=ParaOfficialCatalog.Municipalities.FirstOrDefault(c=>c.NormalizedName==NameNormalizer.Normalize(reference.City))?.Name;
        if(!TavilyWebSearch.IsPublicSourceUrl(reference.SourceUrl) || string.IsNullOrWhiteSpace(reference.PlaceName) || reference.PlaceName.Trim().Length is <3 or >160 ||
            reference.Publisher.Length>160 || reference.SourceKind is not ("official_menu" or "editorial_reference" or "structured_data") ||
            canonicalCity is null || reference.ExpiresAt<=reference.ObservedAt || reference.ObservedAt>DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("Referência inválida.");
        var facts=new Facts(reference.MenuItems.Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim()[..Math.Min(s.Trim().Length,180)]).Distinct().Take(20).ToArray(),
            reference.Address?[..Math.Min(reference.Address.Length,300)],
            (reference.VideoLinks??[]).Where(IsVideo).Distinct().Take(3).ToArray(),(reference.OpeningHours??[]).Select(h=>h[..Math.Min(h.Length,180)]).Distinct().Take(14).ToArray());
        var json=JsonSerializer.Serialize(facts);
        var sourceKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference.SourceUrl+"|"+NameNormalizer.Normalize(reference.PlaceName)+"|"+canonicalCity)));
        await using var db=await factory.OpenAsync(ct);
        // Identical observations retain approval. Changed facts always return to review.
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO place_reference_index(place_name,normalized_name,city,source_url,source_key,source_kind,publisher,observed_at,expires_at,facts_json)
            VALUES(@name,@normalized,@city,@url,@sourceKey,@kind,@publisher,@observed,@expires,@json)
            ON DUPLICATE KEY UPDATE id=LAST_INSERT_ID(id),
              status=CASE WHEN facts_json=@json AND source_kind=@kind AND status IN ('approved','rejected') THEN status ELSE 'pending' END,
              place_name=@name,publisher=@publisher,source_kind=@kind,observed_at=@observed,expires_at=@expires,facts_json=@json
            """,new{name=reference.PlaceName.Trim(),normalized=NameNormalizer.Normalize(reference.PlaceName),city=canonicalCity,url=reference.SourceUrl,
                sourceKey,kind=reference.SourceKind,publisher=reference.Publisher,observed=reference.ObservedAt,expires=reference.ExpiresAt,json},cancellationToken:ct));
        return await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT id FROM place_reference_index WHERE source_key=@sourceKey",new{sourceKey},cancellationToken:ct));
    }

    public async Task<IReadOnlyList<PlaceReference>> ListPendingAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        return (await db.QueryAsync<Row>(new CommandDefinition("SELECT * FROM place_reference_index WHERE status='pending' ORDER BY id LIMIT 50",cancellationToken:ct))).Select(Map).ToArray();
    }

    public async Task<bool> ReviewAsync(long id,string decision,string actor,CancellationToken ct)
    {
        if(decision is not ("approved" or "rejected"))throw new ArgumentException("Decisão inválida.");
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var changed=await db.ExecuteAsync(new CommandDefinition("UPDATE place_reference_index SET status=@decision,reviewed_by=@actor,reviewed_at=UTC_TIMESTAMP(6) WHERE id=@id AND status='pending'",new{id,decision,actor},tx,cancellationToken:ct));
        if(changed==0)return false;
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'reference.review','place_reference',@id,@metadata,UTC_TIMESTAMP(6))",new{actor,id,metadata=JsonSerializer.Serialize(new{decision})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return true;
    }

    public static bool IsVideo(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https"&&string.IsNullOrEmpty(u.UserInfo)&&
        (u.Host is "www.youtube.com" or "youtube.com" or "youtu.be" or "vimeo.com" or "www.vimeo.com");
    private sealed record Facts(string[] MenuItems,string? Address,string[] VideoLinks,string[]? OpeningHours=null);
    private static PlaceReference Map(Row row)
    {
        var facts=JsonSerializer.Deserialize<Facts>(row.facts_json)!;
        return new(row.id,row.place_name,row.city,row.source_url,row.source_kind,row.publisher,new(DateTime.SpecifyKind(row.observed_at,DateTimeKind.Utc)),
            new(DateTime.SpecifyKind(row.expires_at,DateTimeKind.Utc)),facts.MenuItems,facts.Address,facts.VideoLinks,row.status,facts.OpeningHours);
    }
    private sealed class Row
    {
        public long id{get;set;} public string place_name{get;set;}=""; public string city{get;set;}="";
        public string source_url{get;set;}=""; public string source_kind{get;set;}=""; public string publisher{get;set;}="";
        public DateTime observed_at{get;set;} public DateTime expires_at{get;set;} public string facts_json{get;set;}="";public string status{get;set;}="";
    }
}
