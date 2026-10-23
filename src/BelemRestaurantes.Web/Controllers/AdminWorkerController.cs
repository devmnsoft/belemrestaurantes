using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="PlatformOperator"),Route("admin/operacao")]
public sealed class AdminWorkerController(MySqlConnectionFactory factory,IDiscoveryQueue queue,CuiaFreeTierOptions aiLimits):Controller
{
    [HttpPost("manutencao"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Maintenance(string kind,[FromServices] MySqlMaintenanceJobs jobs,CancellationToken ct)
    {if(kind is not("hours" or "mobility"))return BadRequest();var id=await jobs.EnqueueAsync(kind,User.Identity!.Name!,ct);TempData["Flash"]=$"Verificação #{id} enfileirada. O Worker conferirá vigência e pendências dos dados cadastrados; esta ação não importa horários ou tarifas de fontes externas.";return RedirectToAction(nameof(Index));}
    [HttpPost("plano-estadual"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Statewide([FromServices] MySqlStatewideDiscoveryPlan plan,CancellationToken ct)
    {var count=await plan.ConfigureAsync(User.Identity!.Name!,ct);TempData["Flash"]=$"Plano estadual configurado para {count} municípios. Novos alvos escalonados a cada dois minutos, atualização semanal e execução pelo Worker.";return RedirectToAction(nameof(Index));}
    [HttpPost("enriquecer"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var locked=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT GET_LOCK('belrestaurante:manual-enrichment',5)",cancellationToken:ct));
        if(locked!=1)return Conflict("Outra seleção de enriquecimento está em preparação.");
        try{
        await using var tx=await db.BeginTransactionAsync(ct);
        var count=await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO restaurant_enrichment_queue(restaurant_id,priority,state,next_attempt_at)
            SELECT r.id,1,'pending',UTC_TIMESTAMP(6) FROM restaurants r
            WHERE r.status IN ('pending','published') AND r.website IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM restaurant_enrichment_queue q WHERE q.restaurant_id=r.id AND q.state IN ('pending','running'))
              AND NOT EXISTS(SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id=r.id)
            ORDER BY r.updated_at,r.id LIMIT 100
            """,transaction:tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,'crawler.enrichment_enqueue','crawler',@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,metadata=JsonSerializer.Serialize(new{count})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);TempData["Flash"]=$"{count} cadastros enfileirados para enriquecimento pelo Worker. Fontes e regras de acesso serão verificadas antes da consulta.";return RedirectToAction(nameof(Index));
        }finally{await db.ExecuteAsync(new CommandDefinition("SELECT RELEASE_LOCK('belrestaurante:manual-enrichment')",cancellationToken:CancellationToken.None));}
    }
    [HttpGet]
    public async Task<IActionResult> Index(string? city=null,int page=1,CancellationToken ct=default)
    {
        await using var db=await factory.OpenAsync(ct);
        ViewData["AiLimits"]=aiLimits;
        ViewData["FreeUsage"]=(await db.QueryAsync(new CommandDefinition("SELECT service,period,requests FROM external_free_usage WHERE service IN ('cuia_gemini','worker_gemini','cuia_groq','worker_groq') AND period IN (DATE_FORMAT(UTC_DATE(),'%Y%m00'),DATE_FORMAT(UTC_DATE(),'%Y%m%d')) ORDER BY service,period",cancellationToken:ct))).AsList();
        ViewData["Heartbeats"]=(await db.QueryAsync(new CommandDefinition("SELECT * FROM worker_heartbeats ORDER BY heartbeat_at DESC LIMIT 10",cancellationToken:ct))).AsList();
        ViewData["Queue"]=(await db.QueryAsync(new CommandDefinition("SELECT state,COUNT(*) total FROM discovery_jobs GROUP BY state",cancellationToken:ct))).AsList();
        ViewData["Maintenance"]=(await db.QueryAsync(new CommandDefinition("SELECT id,kind,state,attempts,result_json,last_error,created_at,completed_at FROM operational_maintenance_jobs ORDER BY id DESC LIMIT 8",cancellationToken:ct))).AsList();
        var term=string.IsNullOrWhiteSpace(city)?null:"%"+city.Trim()[..Math.Min(city.Trim().Length,100)].Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        var total=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM discovery_targets WHERE @term IS NULL OR city LIKE @term",new{term},cancellationToken:ct));
        page=Math.Clamp(page,1,Math.Max(1,(total+29)/30));
        ViewData["Targets"]=(await db.QueryAsync(new CommandDefinition("SELECT * FROM discovery_targets WHERE @term IS NULL OR city LIKE @term ORDER BY enabled DESC,priority DESC,city,category LIMIT 30 OFFSET @offset",new{term,offset=(page-1)*30},cancellationToken:ct))).AsList();
        var targetCount=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM discovery_targets",cancellationToken:ct));
        var enabledCount=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM discovery_targets WHERE enabled=TRUE",cancellationToken:ct));
        var pendingCount=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurants WHERE status='pending'",cancellationToken:ct));
        var publishedCount=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurants WHERE status='published'",cancellationToken:ct));
        ViewData["TargetCount"]=targetCount;
        ViewData["EnabledCount"]=enabledCount;
        ViewData["PendingCount"]=pendingCount;
        ViewData["PublishedCount"]=publishedCount;
        ViewData["FilteredCount"]=total;ViewData["Page"]=page;ViewData["City"]=city;
        ViewData["Municipalities"]=(await db.QueryAsync<string>(new CommandDefinition("SELECT name FROM geo_municipalities ORDER BY name",cancellationToken:ct))).AsList();
        return View();
    }
    [HttpPost("novo-alvo"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string city,string category,string provider,int priority,int refreshMinutes,int resultLimit,bool enabled,CancellationToken ct)
    {
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(city)||city.Length>100||string.IsNullOrWhiteSpace(category)||category.Length>100
            ||provider is not ("default" or "openstreetmap" or "googleplaces")||priority is <1 or >10||refreshMinutes is <60 or >525600||resultLimit is <1 or >2000)return BadRequest();
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var normalized=BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(city);
        var official=await db.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT name FROM geo_municipalities WHERE normalized_name=@normalized LIMIT 1",new{normalized},tx,cancellationToken:ct));
        if(official is null)return BadRequest("Selecione um município oficial do Pará.");
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO discovery_targets(city,normalized_city,category,provider,priority,refresh_minutes,result_limit,enabled,next_run_at)
            VALUES(@official,@normalized,@category,@provider,@priority,@refreshMinutes,@resultLimit,@enabled,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE provider=@provider,priority=@priority,refresh_minutes=@refreshMinutes,result_limit=@resultLimit,enabled=@enabled
            """,new{official,normalized,category=category.Trim(),provider,priority,refreshMinutes,resultLimit,enabled},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,'discovery.target_upsert','discovery_target',@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,metadata=JsonSerializer.Serialize(new{city=official,category,provider,priority,refreshMinutes,resultLimit,enabled})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);TempData["Flash"]="Alvo cadastrado. O Worker enfileirará o trabalho conforme frequência e disponibilidade da fonte.";
        return RedirectToAction(nameof(Index),new{city=official});
    }
    [HttpPost("sincronizar"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var count=await queue.EnqueueDueTargetsAsync(DateTimeOffset.UtcNow,ct);
        await using var db=await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,'discovery.enqueue_now','discovery',@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,metadata=JsonSerializer.Serialize(new{count})},cancellationToken:ct));
        TempData["Flash"]=$"{count} tarefas enfileiradas. O Worker consultará as fontes; tarefas existentes foram preservadas.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost("alvo"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Target(long id,bool enabled,string provider,int priority,int refreshMinutes,int resultLimit,CancellationToken ct)
    {
        if(provider is not ("default" or "openstreetmap" or "googleplaces")||priority is <1 or >10||refreshMinutes is <60 or >525600||resultLimit is <1 or >2000)return BadRequest();
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        var changed=await db.ExecuteAsync(new CommandDefinition("UPDATE discovery_targets SET enabled=@enabled,provider=@provider,priority=@priority,refresh_minutes=@refreshMinutes,result_limit=@resultLimit,next_run_at=CASE WHEN enabled THEN LEAST(next_run_at,UTC_TIMESTAMP(6)) ELSE next_run_at END WHERE id=@id",new{id,enabled,provider,priority,refreshMinutes,resultLimit},tx,cancellationToken:ct));
        if(changed>0)await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'discovery.target_configure','discovery_target',@id,@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,id,metadata=JsonSerializer.Serialize(new{enabled,provider,priority,refreshMinutes,resultLimit})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);TempData["Flash"]="Alvo atualizado. A frequência é independente do agendador de um minuto.";return RedirectToAction(nameof(Index));
    }
}
