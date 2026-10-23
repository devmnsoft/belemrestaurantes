using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

public sealed class CulinaryTaskStatus
{
    public long Id {get;set;}
    public string Topic {get;set;}="";
    public string State {get;set;}="";
    public string? LastStatus {get;set;}
    public int Attempts {get;set;}
    public DateTime UpdatedAt {get;set;}
}
public sealed record CulinaryAdminPage(int References,int Videos,int Dishes,IReadOnlyList<CulinaryTaskStatus> Tasks);

[Authorize(Policy="RestaurantModerator")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminCulinaryController(MySqlConnectionFactory factory):Controller
{
    [HttpGet("/admin/pesquisa-culinaria")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var references=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM culinary_references WHERE state='published'",cancellationToken:ct));
        var videos=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM culinary_videos",cancellationToken:ct));
        var dishes=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM place_dishes",cancellationToken:ct));
        var tasks=(await db.QueryAsync<CulinaryTaskStatus>(new CommandDefinition("SELECT id,topic,state,last_status LastStatus,attempts,updated_at UpdatedAt FROM culinary_tasks ORDER BY updated_at DESC LIMIT 40",cancellationToken:ct))).ToArray();
        return View(new CulinaryAdminPage(references,videos,dishes,tasks));
    }
}
