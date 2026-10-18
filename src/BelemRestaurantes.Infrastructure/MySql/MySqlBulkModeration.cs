using System.Text.Json;
using Dapper;
using BelemRestaurantes.Application.Models;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlBulkModeration(MySqlConnectionFactory factory, IConfiguration configuration)
{
    public int Limit => Math.Clamp(configuration.GetValue("Moderation:BatchLimit", 1000), 1, 2000);
    internal const string Select = """
        SELECT r.id Id,r.name Name,r.city City,r.category Category,r.address Address,r.neighborhood Neighborhood,
        r.phone Phone,r.created_source_url SourceUrl,r.google_place_id ExternalId,r.status Status,r.created_at DiscoveredAt,
        CASE WHEN r.status IN ('published','rejected') THEN r.status WHEN r.status='suspended' THEN CASE WHEN r.moderation_stage='blocked' THEN 'blocked' ELSE 'archived' END ELSE r.moderation_stage END Stage,
        (SELECT o.provider FROM source_observations o WHERE o.restaurant_id=r.id ORDER BY o.last_seen_at DESC,o.id DESC LIMIT 1) Source,
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
        var from=" FROM restaurants r"+FilteredWhere(filter);
        var parameters=Parameters(filter);
        // Count only the requested filter. Expensive checks are evaluated here only when filtered.
        var count=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*)"+from,parameters,cancellationToken:ct));
        var pageSize=filter.SafePageSize;
        page=Math.Clamp(page,1,Math.Max(1,(int)Math.Ceiling(count/(double)pageSize)));
        parameters.Add("offset",(page-1)*pageSize);
        parameters.Add("pageSize",pageSize);
        // Slice IDs before loading observations and duplicate flags, including on deep pages.
        var ids=(await db.QueryAsync<long>(new CommandDefinition("SELECT r.id"+from+Order(filter)+" LIMIT @pageSize OFFSET @offset",parameters,cancellationToken:ct))).ToArray();
        var rows=ids.Length==0?new List<ModerationCandidate>():(await db.QueryAsync<ModerationCandidate>(new CommandDefinition(Select+" WHERE r.id IN @ids"+Order(filter),new{ids},cancellationToken:ct))).AsList();
        return new(rows,page,pageSize,count);
    }
    private async Task<List<ModerationCandidate>> CandidatesAsync(ModerationFilter f, CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var ids=(await db.QueryAsync<long>(new CommandDefinition("SELECT r.id FROM restaurants r"+FilteredWhere(f)+Order(f)+" LIMIT @limit",Parameters(f),cancellationToken:ct))).ToArray();
        return ids.Length==0?[]:(await db.QueryAsync<ModerationCandidate>(new CommandDefinition(Select+" WHERE r.id IN @ids"+Order(f),new{ids},cancellationToken:ct))).AsList();
    }
    private const string SourceCheck="(SELECT o.provider FROM source_observations o WHERE o.restaurant_id=r.id ORDER BY o.last_seen_at DESC,o.id DESC LIMIT 1)";
    private const string CityCheck="EXISTS(SELECT 1 FROM geo_municipalities g WHERE g.normalized_name=r.normalized_city)";
    private const string DuplicateCheck="""
        (EXISTS(SELECT 1 FROM moderation_queue m WHERE m.item_type='restaurant' AND m.item_id=r.id
          AND m.status='pending' AND m.reason IN ('DuplicateSuspected','PossibleDuplicate','Duplicate','DeduplicationConflict'))
        OR EXISTS(SELECT 1 FROM restaurants d WHERE d.id<>r.id AND d.status IN ('pending','published')
          AND d.normalized_city=r.normalized_city AND d.normalized_name=r.normalized_name
          AND COALESCE(d.address,'')=COALESCE(r.address,'')))
        """;
    private static string FilteredWhere(ModerationFilter f)
    {
        var sql=new System.Text.StringBuilder(" WHERE ");
        sql.Append(f.Stage switch {
            "published"=>"r.status='published'", "rejected"=>"r.status='rejected'",
            "archived"=>"r.status='suspended' AND r.moderation_stage<>'blocked'",
            "blocked"=>"r.status='suspended' AND r.moderation_stage='blocked'",
            "approved"=>"r.status='pending' AND r.moderation_stage='approved'",
            _=>"r.status='pending' AND r.moderation_stage='pending'" });
        if(!string.IsNullOrWhiteSpace(f.Term))sql.Append(" AND (r.name LIKE @term OR r.address LIKE @term OR r.google_place_id LIKE @term)");
        if(!string.IsNullOrWhiteSpace(f.City))sql.Append(" AND r.city=@city");
        if(!string.IsNullOrWhiteSpace(f.Category))sql.Append(" AND r.category=@category");
        if(f.From is not null)sql.Append(" AND r.created_at>=@from");
        if(f.Until is not null)sql.Append(" AND r.created_at<@until");
        if(!string.IsNullOrWhiteSpace(f.Source))sql.Append(" AND "+SourceCheck+"=@source");
        if(f.Duplicate is "yes" or "no")sql.Append(" AND "+DuplicateCheck+"=@duplicate");
        if(f.Completeness is "eligible" or "incomplete")sql.Append(" AND COALESCE(("+CityCheck+" AND NOT "+DuplicateCheck+" AND CHAR_LENGTH(TRIM(r.name))>=2 AND NULLIF(TRIM(r.category),'') IS NOT NULL AND NULLIF(TRIM(r.address),'') IS NOT NULL AND r.created_source_url LIKE 'https://%'),0)=@eligible");
        return sql.ToString();
    }
    private DynamicParameters Parameters(ModerationFilter f)
    {
        return new DynamicParameters(new { term=string.IsNullOrWhiteSpace(f.Term)?null:"%"+MySqlRestaurantReadRepository.EscapeLike(f.Term.Trim()[..Math.Min(f.Term.Trim().Length,160)])+"%",
            city=Clean(f.City),category=Clean(f.Category),source=Clean(f.Source),from=f.From?.Date,until=f.Until?.Date.AddDays(1),
            duplicate=f.Duplicate=="yes"?(bool?)true:f.Duplicate=="no"?false:null,
            eligible=f.Completeness=="eligible"?(bool?)true:f.Completeness=="incomplete"?false:null,limit=Limit,
            stage=f.Stage is "approved" or "published" or "rejected" or "archived" or "blocked"?f.Stage:"pending" });
    }
    private static string Order(ModerationFilter f)=>f.Sort switch{"name"=>" ORDER BY r.name,r.id","recent"=>" ORDER BY r.created_at DESC,r.id DESC","city"=>" ORDER BY r.city,r.name,r.id",_=>" ORDER BY r.created_at,r.id"};
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text)?null:text.Trim()[..Math.Min(text.Trim().Length,160)];
    public async Task<ModerationBatch> PrepareAsync(string actor, string operation, string? reason, long[] ids,
        bool allFiltered, ModerationFilter filter, CancellationToken ct)
    {
        if(operation is not ("approve" or "approve_only" or "publish" or "archive" or "block" or "reject" or "review" or "duplicate" or "reprocess")) throw new ArgumentException("Operação inválida.");
        if(reason?.Length>500 || operation is not("approve" or "approve_only" or "publish") && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Informe um motivo de até 500 caracteres.");
        var candidates=allFiltered?await CandidatesAsync(filter,ct):new List<ModerationCandidate>();
        if(!allFiltered && ids.Length>0)
        {
            if(ids.Length>Limit)throw new ArgumentException($"O limite é {Limit} cadastros.");
            await using var selectionDb=await factory.OpenAsync(ct);
            var parameters=Parameters(filter);parameters.Add("ids",ids.Distinct().ToArray());
            candidates=(await selectionDb.QueryAsync<ModerationCandidate>(new CommandDefinition(Select+FilteredWhere(filter)+" AND r.id IN @ids",parameters,cancellationToken:ct))).AsList();
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
            var blocked=operation is "archive" or "block"?(row.Stage is "pending" or "approved" or "published"?null:"O cadastro já está fora do fluxo ativo.")
                :row.Status!="pending"?"O cadastro já recebeu uma decisão."
                :operation=="publish"&&row.Stage!="approved"?"A publicação separada exige aprovação prévia."
                :operation=="approve_only"&&row.Stage!="pending"?"Este cadastro já foi aprovado."
                :operation is "approve" or "approve_only" or "publish"?row.BlockReason:null;
            if(blocked is not null) { results.Add(new(row.Id,row.Name,"ignored",blocked)); continue; }
            if(operation is "approve" or "publish" or "approve_only" or "reject" or "archive" or "block")
            {
                var status=operation is "approve" or "publish"?"published":operation=="approve_only"?"pending":operation=="reject"?"rejected":"suspended";
                var stage=operation=="approve_only"?"approved":operation=="archive"?"archived":operation=="block"?"blocked":status;
                await db.ExecuteAsync(new CommandDefinition("UPDATE restaurants SET status=@status,moderation_stage=@stage,published_at=CASE WHEN @status='published' THEN UTC_TIMESTAMP(6) ELSE published_at END WHERE id=@itemId",new{status,stage,itemId},tx,cancellationToken:ct));
                await db.ExecuteAsync(new CommandDefinition("UPDATE moderation_queue SET status=@state,reviewed_by=@actor,reviewed_at=UTC_TIMESTAMP(6) WHERE item_type='restaurant' AND item_id=@itemId AND status='pending'",new{state=operation is "approve" or "approve_only" or "publish"?"approved":"rejected",actor,itemId},tx,cancellationToken:ct));
            }
            else if(operation=="reprocess")
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO restaurant_enrichment_queue(restaurant_id,priority,state,next_attempt_at)
                    SELECT @itemId,1,'pending',UTC_TIMESTAMP(6) FROM DUAL
                    WHERE NOT EXISTS(SELECT 1 FROM restaurant_enrichment_queue WHERE restaurant_id=@itemId AND state IN ('pending','running'))
                    """,new{itemId},tx,cancellationToken:ct));
            else
            {
                await db.ExecuteAsync(new CommandDefinition("UPDATE restaurants SET moderation_stage='pending' WHERE id=@itemId",new{itemId},tx,cancellationToken:ct));
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO moderation_queue(item_type,item_id,reason,detail,status,created_at)
                    SELECT 'restaurant',@itemId,@code,@reason,'pending',UTC_TIMESTAMP(6) FROM DUAL
                    WHERE NOT EXISTS(SELECT 1 FROM moderation_queue WHERE item_type='restaurant' AND item_id=@itemId AND reason=@code AND status='pending')
                    """,new{itemId,code=operation=="duplicate"?"PossibleDuplicate":"DataConflict",reason},tx,cancellationToken:ct));
            }
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,@action,'restaurant',@itemId,@metadata,UTC_TIMESTAMP(6))",
                new{actor,action="restaurant.bulk_"+operation,itemId,metadata=JsonSerializer.Serialize(new{batchId=id,reason})},tx,cancellationToken:ct));
            results.Add(new(itemId,row.Name,"applied",operation is "approve" or "publish"?"Publicado no catálogo.":operation=="approve_only"?"Aprovado; aguardando publicação.":operation=="archive"?"Arquivado e retirado do catálogo público.":operation=="block"?"Bloqueado e retirado do catálogo público.":operation=="reject"?"Rejeitado com motivo.":"Encaminhado para análise."));
        }
        await db.ExecuteAsync(new CommandDefinition("UPDATE moderation_batches SET completed_at=UTC_TIMESTAMP(6),result_json=@result WHERE id=@id",new{id,result=JsonSerializer.Serialize(results)},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(id,operation,reason,[],results);
    }
}
