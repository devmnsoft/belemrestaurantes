using System.Text.Json;
using Dapper;
using BelemRestaurantes.Application.Models;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlBulkModeration(MySqlConnectionFactory factory, IConfiguration configuration)
{
    public int Limit => Math.Clamp(configuration.GetValue("Moderation:BatchLimit", 500), 1, 1000);
    internal const string Select = """
        SELECT r.id Id,r.name Name,r.city City,r.category Category,r.address Address,r.neighborhood Neighborhood,
        r.phone Phone,r.created_source_url SourceUrl,r.google_place_id ExternalId,r.status Status,r.created_at DiscoveredAt,
        (SELECT o.provider FROM source_observations o WHERE o.restaurant_id=r.id ORDER BY o.last_seen_at DESC LIMIT 1) Source,
        EXISTS(SELECT 1 FROM geo_municipalities g WHERE g.normalized_name=r.normalized_city) ValidCity,
        (EXISTS(SELECT 1 FROM moderation_queue m WHERE m.item_type='restaurant' AND m.item_id=r.id
             AND m.status='pending' AND m.reason IN ('DuplicateSuspected','PossibleDuplicate','Duplicate','DeduplicationConflict'))
         OR EXISTS(SELECT 1 FROM restaurants d WHERE d.id<>r.id AND d.status IN ('pending','published')
            AND d.normalized_city=r.normalized_city AND d.normalized_name=r.normalized_name
            AND COALESCE(d.address,'')=COALESCE(r.address,''))) Duplicate
        FROM restaurants r
        """;
    public async Task<PagedResult<ModerationCandidate>> ListAsync(ModerationFilter filter, int page, CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var sql=FilteredSql(filter);
        var parameters=Parameters(filter);
        var count=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ("+sql+") filtered",parameters,cancellationToken:ct));
        page=Math.Clamp(page,1,Math.Max(1,(count+29)/30));
        parameters.Add("offset",(page-1)*30);
        var rows=(await db.QueryAsync<ModerationCandidate>(new CommandDefinition(sql+" ORDER BY DiscoveredAt,Id LIMIT 30 OFFSET @offset",parameters,cancellationToken:ct))).AsList();
        return new(rows,page,30,count);
    }
    private async Task<List<ModerationCandidate>> CandidatesAsync(ModerationFilter f, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        return (await db.QueryAsync<ModerationCandidate>(new CommandDefinition(FilteredSql(f)+" ORDER BY DiscoveredAt,Id LIMIT @limit",Parameters(f),cancellationToken:ct))).AsList();
    }
    private static string FilteredSql(ModerationFilter f)=>"SELECT * FROM ("+Select+"\n"+"""
            WHERE r.status='pending' AND (@term IS NULL OR r.name LIKE @term OR r.address LIKE @term OR r.google_place_id LIKE @term)
            AND (@city IS NULL OR r.city=@city) AND (@category IS NULL OR r.category=@category)
            AND (@from IS NULL OR r.created_at>=@from) AND (@until IS NULL OR r.created_at<@until)
            ) candidates WHERE (@source IS NULL OR Source=@source)
            AND (@duplicate IS NULL OR Duplicate=@duplicate)
            AND (@eligible IS NULL OR COALESCE((ValidCity=1 AND Duplicate=0 AND CHAR_LENGTH(TRIM(Name))>=2
                AND NULLIF(TRIM(Category),'') IS NOT NULL AND NULLIF(TRIM(Address),'') IS NOT NULL
                AND SourceUrl LIKE 'https://%'),0)=@eligible)
            """;
    private DynamicParameters Parameters(ModerationFilter f)
    {
        return new DynamicParameters(new { term=string.IsNullOrWhiteSpace(f.Term)?null:"%"+MySqlRestaurantReadRepository.EscapeLike(f.Term.Trim()[..Math.Min(f.Term.Trim().Length,160)])+"%",
            city=Clean(f.City),category=Clean(f.Category),source=Clean(f.Source),from=f.From?.Date,until=f.Until?.Date.AddDays(1),
            duplicate=f.Duplicate=="yes"?(bool?)true:f.Duplicate=="no"?false:null,
            eligible=f.Completeness=="eligible"?(bool?)true:f.Completeness=="incomplete"?false:null,limit=Limit+1 });
    }
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text)?null:text.Trim()[..Math.Min(text.Trim().Length,160)];
    public async Task<ModerationBatch> PrepareAsync(string actor, string operation, string? reason, long[] ids,
        bool allFiltered, ModerationFilter filter, CancellationToken ct)
    {
        if(operation is not ("approve" or "reject" or "review" or "duplicate" or "reprocess")) throw new ArgumentException("Operação inválida.");
        if(operation != "approve" && (string.IsNullOrWhiteSpace(reason) || reason.Length>500)) throw new ArgumentException("Informe um motivo de até 500 caracteres.");
        var candidates=await CandidatesAsync(filter,ct);
        if(!allFiltered && ids.Length>0)
        {
            if(ids.Length>Limit)throw new ArgumentException($"O limite é {Limit} cadastros.");
            await using var selectionDb=await factory.OpenAsync(ct);
            var parameters=Parameters(filter);parameters.Add("ids",ids.Distinct().ToArray());
            candidates=(await selectionDb.QueryAsync<ModerationCandidate>(new CommandDefinition(FilteredSql(filter)+" AND Id IN @ids",parameters,cancellationToken:ct))).AsList();
        }
        var selected=allFiltered?candidates:candidates.Where(c=>ids.Contains(c.Id)).ToList();
        if(selected.Count==0 || selected.Count>Limit) throw new ArgumentException($"Selecione entre 1 e {Limit} cadastros; refine os filtros para dividir a operação.");
        var id=Guid.NewGuid().ToString();
        await using var db=await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO moderation_batches(id,actor,operation,reason,created_at,expires_at,selection_json)
            VALUES(@id,@actor,@operation,@reason,UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 15 MINUTE),@selection)
            """,new{id,actor,operation,reason,selection=JsonSerializer.Serialize(selected.Select(s=>s.Id))},cancellationToken:ct));
        return new(id,operation,reason,selected);
    }
    public async Task<ModerationBatch?> ExecuteAsync(string id,string actor,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        var batch=await db.QuerySingleOrDefaultAsync(new CommandDefinition("SELECT * FROM moderation_batches WHERE id=@id AND actor=@actor FOR UPDATE",new{id,actor},tx,cancellationToken:ct));
        if(batch is null) return null;
        string operation=batch.operation;
        string? reason=batch.reason;
        if(batch.result_json is string completed) return new(id,operation,reason,[],JsonSerializer.Deserialize<ModerationDecision[]>(completed));
        if((DateTime)batch.expires_at<DateTime.UtcNow) throw new ArgumentException("A confirmação expirou. Refaça a seleção.");
        var ids=JsonSerializer.Deserialize<long[]>((string)batch.selection_json)!;
        // Lock in deterministic order before evaluating duplicates or changing any row.
        await db.QueryAsync<long>(new CommandDefinition("SELECT id FROM restaurants WHERE id IN @ids ORDER BY id FOR UPDATE",new{ids},tx,cancellationToken:ct));
        var rows=(await db.QueryAsync<ModerationCandidate>(new CommandDefinition(Select+" WHERE r.id IN @ids ORDER BY r.id",new{ids},tx,cancellationToken:ct))).ToDictionary(r=>r.Id);
        var results=new List<ModerationDecision>();
        foreach(var itemId in ids)
        {
            if(!rows.TryGetValue(itemId,out var row)) { results.Add(new(itemId,"Cadastro removido","ignored","O cadastro não existe mais.")); continue; }
            var blocked=row.Status!="pending"?"O cadastro já recebeu uma decisão.":operation=="approve"?row.BlockReason:null;
            if(blocked is not null) { results.Add(new(row.Id,row.Name,"ignored",blocked)); continue; }
            if(operation is "approve" or "reject")
            {
                var status=operation=="approve"?"published":"rejected";
                await db.ExecuteAsync(new CommandDefinition("UPDATE restaurants SET status=@status,published_at=CASE WHEN @status='published' THEN UTC_TIMESTAMP(6) ELSE published_at END WHERE id=@itemId AND status='pending'",new{status,itemId},tx,cancellationToken:ct));
                await db.ExecuteAsync(new CommandDefinition("UPDATE moderation_queue SET status=@state,reviewed_by=@actor,reviewed_at=UTC_TIMESTAMP(6) WHERE item_type='restaurant' AND item_id=@itemId AND status='pending'",new{state=operation=="approve"?"approved":"rejected",actor,itemId},tx,cancellationToken:ct));
            }
            else if(operation=="reprocess")
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO restaurant_enrichment_queue(restaurant_id,priority,state,next_attempt_at)
                    SELECT @itemId,1,'pending',UTC_TIMESTAMP(6) FROM DUAL
                    WHERE NOT EXISTS(SELECT 1 FROM restaurant_enrichment_queue WHERE restaurant_id=@itemId AND state IN ('pending','running'))
                    """,new{itemId},tx,cancellationToken:ct));
            else
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO moderation_queue(item_type,item_id,reason,detail,status,created_at)
                    SELECT 'restaurant',@itemId,@code,@reason,'pending',UTC_TIMESTAMP(6) FROM DUAL
                    WHERE NOT EXISTS(SELECT 1 FROM moderation_queue WHERE item_type='restaurant' AND item_id=@itemId AND reason=@code AND status='pending')
                    """,new{itemId,code=operation=="duplicate"?"PossibleDuplicate":"DataConflict",reason},tx,cancellationToken:ct));
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,@action,'restaurant',@itemId,@metadata,UTC_TIMESTAMP(6))",
                new{actor,action="restaurant.bulk_"+operation,itemId,metadata=JsonSerializer.Serialize(new{batchId=id,reason})},tx,cancellationToken:ct));
            results.Add(new(itemId,row.Name,"applied",operation=="approve"?"Publicado no catálogo.":operation=="reject"?"Rejeitado com motivo.":"Encaminhado para análise."));
        }
        await db.ExecuteAsync(new CommandDefinition("UPDATE moderation_batches SET completed_at=UTC_TIMESTAMP(6),result_json=@result WHERE id=@id",new{id,result=JsonSerializer.Serialize(results)},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(id,operation,reason,[],results);
    }
}
