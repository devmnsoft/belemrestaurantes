using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="ContentEditor"),Route("admin/midias")]
public sealed class AdminMediaController(MySqlConnectionFactory factory, IRestaurantMediaRepository media) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long restaurantId,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var name=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT name FROM restaurants WHERE id=@restaurantId",new{restaurantId},cancellationToken:ct));
        if(name is null)return NotFound();
        ViewData["RestaurantId"]=restaurantId;ViewData["RestaurantName"]=name;
        return View(await media.GetMediaByRestaurantAsync(restaurantId,false,ct));
    }

    [HttpPost("video"),ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVideo(long restaurantId,string? url,string? title,string? sourceUrl,bool reviewed,CancellationToken ct)
    {
        var video=RestaurantVideo.From(url);
        if(video is null || string.IsNullOrWhiteSpace(title) || title.Length>255 || !reviewed ||
            sourceUrl is null || sourceUrl.Length>255 || !Uri.TryCreate(sourceUrl,UriKind.Absolute,out var source) ||
            source.Scheme!="https" || !string.IsNullOrEmpty(source.UserInfo))
        {TempData["ErrorMessage"]="Informe um link de vídeo válido, título e fonte HTTPS. Confirme a relação do vídeo com este restaurante.";return RedirectToAction(nameof(Index),new{restaurantId});}
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM restaurants WHERE id=@restaurantId",new{restaurantId},tx,cancellationToken:ct))!=1)return NotFound();
        var id=await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO restaurant_media(restaurant_id,media_type,media_url,source_origin,attribution_text,license_type,status,priority_tier)
            VALUES(@restaurantId,'video_embed',@url,@source,@title,'external_link','pending',3);
            SELECT LAST_INSERT_ID();
            """,new{restaurantId,url=video.Url,source=source.AbsoluteUri,title=title.Trim()},tx,cancellationToken:ct));
        await Audit(db,tx,id,"video_submitted",ct);
        await tx.CommitAsync(ct);
        TempData["Flash"]="Vídeo enviado para revisão. Aparecerá no restaurante após aprovação.";
        return RedirectToAction(nameof(Index),new{restaurantId});
    }

    [Authorize(Policy="RestaurantModerator"),HttpPost("decidir"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(long id,long restaurantId,string decision,CancellationToken ct)
    {
        if(decision is not("approved" or "rejected"))return BadRequest();
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        var url=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT media_url FROM restaurant_media WHERE id=@id AND restaurant_id=@restaurantId AND media_type='video_embed' FOR UPDATE",new{id,restaurantId},tx,cancellationToken:ct));
        if(url is null)return NotFound();
        if(decision=="approved"&&RestaurantVideo.From(url) is null)return BadRequest();
        await db.ExecuteAsync(new CommandDefinition("UPDATE restaurant_media SET status=@decision WHERE id=@id",new{id,decision},tx,cancellationToken:ct));
        await Audit(db,tx,id,"video_"+decision,ct);
        await tx.CommitAsync(ct);
        TempData["Flash"]=decision=="approved"?"Vídeo aprovado para exibição pública.":"Vídeo retirado da exibição pública.";
        return RedirectToAction(nameof(Index),new{restaurantId});
    }
    private Task Audit(MySqlConnector.MySqlConnection db,MySqlConnector.MySqlTransaction tx,long id,string action,CancellationToken ct) =>
        db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,occurred_at) VALUES(@actor,@action,'restaurant_media',@id,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,action,id},tx,cancellationToken:ct));
}
