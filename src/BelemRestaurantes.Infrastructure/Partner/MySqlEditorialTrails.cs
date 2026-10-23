using System.Text.Json;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.Ai;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Partner;

public sealed partial class MySqlMobilityTrailService
{
    public async Task<IReadOnlyList<GastronomicTrail>> ListForEditingAsync(CancellationToken ct)
    {
        await using var db=await connectionFactory.OpenAsync(ct);
        return (await db.QueryAsync<GastronomicTrail>(new CommandDefinition("SELECT * FROM gastronomic_trails ORDER BY editorial_json IS NULL,title",cancellationToken:ct))).AsList();
    }
    public async Task<GastronomicTrail?> GetForEditingAsync(int id,CancellationToken ct)
    {
        await using var db=await connectionFactory.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<GastronomicTrail>(new CommandDefinition("SELECT * FROM gastronomic_trails WHERE id=@id",new{id},cancellationToken:ct));
    }
    public static void ValidateEditorial(string title,string description,int hours,TrailEditorial editorial)
    {
        static bool Text(string? s,int min,int max)=>s is not null&&s.Trim().Length>=min&&s.Length<=max;
        if(!Text(title,3,150)||!Text(description,10,1500)||hours is <1 or >24||!Text(editorial.Audience,3,400)||!Text(editorial.Origin,3,300)||!Text(editorial.BeforeYouGo,10,1600)||!Text(editorial.Accessibility,10,800)||editorial.Mode is not("road" or "river" or "urban")||editorial.Stops.Count is <3 or >5)
            throw new ArgumentException("Confira título, resumo, duração, orientações e três a cinco etapas.");
        foreach(var stop in editorial.Stops)
            if(!Text(stop.Name,3,160)||!Text(stop.City,3,100)||!Text(stop.Summary,10,1200)||!Text(stop.SourceName,3,160)||!TavilyWebSearch.IsPublicSourceUrl(stop.SourceUrl))
                throw new ArgumentException("Cada etapa precisa de nome, município, orientação e fonte pública HTTPS identificada.");
    }
    public async Task<bool> SaveDraftAsync(int id,string title,string description,int durationHours,TrailEditorial editorial,int version,string actor,CancellationToken ct)
    {
        ValidateEditorial(title,description,durationHours,editorial);
        return await ChangeAsync(id,version,actor,"trail.edit",async(db,tx)=>
            await db.ExecuteAsync(new CommandDefinition("UPDATE gastronomic_trails SET title=@title,description=@description,estimated_duration_hours=@durationHours,editorial_json=@json,editorial_state='draft',is_active=FALSE,editorial_version=editorial_version+1 WHERE id=@id AND editorial_version=@version",new{id,version,title,description,durationHours,json=JsonSerializer.Serialize(editorial)},tx,cancellationToken:ct)),ct);
    }
    public async Task<bool> SetPublishedAsync(int id,bool published,int version,string actor,CancellationToken ct)
    {
        return await ChangeAsync(id,version,actor,published?"trail.publish":"trail.unpublish",async(db,tx)=>
        {
            var current=await db.QuerySingleAsync<GastronomicTrail>(new CommandDefinition("SELECT * FROM gastronomic_trails WHERE id=@id",new{id},tx,cancellationToken:ct));
            if(published){if(current.Editorial is null)throw new ArgumentException("Adicione e revise as etapas antes de publicar.");ValidateEditorial(current.Title,current.Description,current.EstimatedDurationHours,current.Editorial);}
            return await db.ExecuteAsync(new CommandDefinition("UPDATE gastronomic_trails SET is_active=@published,editorial_state=@state,reviewed_at=CASE WHEN @published THEN UTC_TIMESTAMP(6) ELSE reviewed_at END,editorial_version=editorial_version+1 WHERE id=@id AND editorial_version=@version",new{id,version,published,state=published?"published":"draft"},tx,cancellationToken:ct));
        },ct);
    }
    private async Task<bool> ChangeAsync(int id,int version,string actor,string action,Func<MySqlConnector.MySqlConnection,MySqlConnector.MySqlTransaction,Task<int>> update,CancellationToken ct)
    {
        await using var db=await connectionFactory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var current=await db.QuerySingleOrDefaultAsync<GastronomicTrail>(new CommandDefinition("SELECT * FROM gastronomic_trails WHERE id=@id FOR UPDATE",new{id},tx,cancellationToken:ct));
        if(current is null||current.EditorialVersion!=version)return false;
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO editorial_trail_revisions(trail_id,version,snapshot_json,actor) VALUES(@id,@version,@snapshot,@actor)",new{id,version,snapshot=JsonSerializer.Serialize(current),actor},tx,cancellationToken:ct));
        if(await update(db,tx)!=1)return false;
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,@action,'trail',@id,@metadata,UTC_TIMESTAMP(6)); UPDATE editorial_review_queue SET state='completed' WHERE trail_id=@id AND state='pending'",new{actor,action,id=id.ToString(),metadata=JsonSerializer.Serialize(new{version})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return true;
    }
}
