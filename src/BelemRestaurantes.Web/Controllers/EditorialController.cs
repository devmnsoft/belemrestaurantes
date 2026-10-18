using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Route("admin/editorial"),Authorize(Policy="ContentEditor")]
public sealed class EditorialController(MySqlConnectionFactory factory):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long id=0,CancellationToken ct=default)
    {
        await using var db=await factory.OpenAsync(ct);
        ViewData["Articles"]=(await db.QueryAsync<EditorialArticle>(new CommandDefinition("SELECT id Id,title Title,body Body,source_url SourceUrl,status Status,version Version FROM editorial_articles ORDER BY updated_at DESC LIMIT 100",cancellationToken:ct))).AsList();
        var item=id==0?new EditorialArticle():await db.QuerySingleOrDefaultAsync<EditorialArticle>(new CommandDefinition("SELECT id Id,title Title,body Body,source_url SourceUrl,status Status,version Version FROM editorial_articles WHERE id=@id",new{id},cancellationToken:ct));
        return item is null?NotFound():View(item);
    }
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(65536)]
    public async Task<IActionResult> Save(EditorialArticle article,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(article.Title)||article.Title.Length>160||string.IsNullOrWhiteSpace(article.Body)||article.Body.Length>20000||article.Status is not ("draft" or "published")
            ||!string.IsNullOrWhiteSpace(article.SourceUrl)&&(!Uri.TryCreate(article.SourceUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||article.SourceUrl.Length>350))return BadRequest("Confira título, texto e fonte HTTPS.");
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        int changed;
        if(article.Id==0){changed=await db.ExecuteAsync(new CommandDefinition("INSERT INTO editorial_articles(title,body,source_url,status,updated_by,updated_at) VALUES(@Title,@Body,@SourceUrl,@Status,@actor,UTC_TIMESTAMP(6))",new{article.Title,article.Body,article.SourceUrl,article.Status,actor=User.Identity!.Name},tx,cancellationToken:ct));article.Id=await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()",transaction:tx,cancellationToken:ct));}
        else changed=await db.ExecuteAsync(new CommandDefinition("UPDATE editorial_articles SET title=@Title,body=@Body,source_url=@SourceUrl,status=@Status,updated_by=@actor,updated_at=UTC_TIMESTAMP(6),version=version+1 WHERE id=@Id AND version=@Version",new{article.Id,article.Version,article.Title,article.Body,article.SourceUrl,article.Status,actor=User.Identity!.Name},tx,cancellationToken:ct));
        if(changed!=1)return Conflict("O texto foi alterado por outro editor. Recarregue.");
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at) VALUES(@actor,'editorial.save','editorial_article',@Id,@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,article.Id,metadata=JsonSerializer.Serialize(new{article.Title,article.Status,article.Version})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);TempData["Flash"]="Conteúdo salvo com auditoria.";return RedirectToAction(nameof(Index),new{id=article.Id});
    }
}
public sealed class EditorialArticle
{
    public long Id { get; set; }
    public string Title { get; set; }="";
    public string Body { get; set; }="";
    public string? SourceUrl { get; set; }
    public string Status { get; set; }="draft";
    public int Version { get; set; }
}
[AllowAnonymous,Route("sabores")]
public sealed class PublicEditorialController(MySqlConnectionFactory factory):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {await using var db=await factory.OpenAsync(ct);return View((await db.QueryAsync<EditorialArticle>(new CommandDefinition("SELECT id Id,title Title,body Body,source_url SourceUrl FROM editorial_articles WHERE status='published' ORDER BY updated_at DESC LIMIT 30",cancellationToken:ct))).AsList());}
}
