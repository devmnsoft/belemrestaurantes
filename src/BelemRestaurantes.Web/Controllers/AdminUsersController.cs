using System.Security.Claims;
using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;
[Authorize(Policy="PlatformAdmin"),Route("admin/usuarios")]
public sealed class AdminUsersController(MySqlConnectionFactory factory):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        return View((await db.QueryAsync<AdminAccessRow>(new CommandDefinition("""
            SELECT u.id Id,u.user_name UserName,(u.disabled OR u.is_disabled) Disabled,u.two_factor_enabled Mfa,
                u.must_change_password MustChangePassword,GROUP_CONCAT(r.name ORDER BY r.name) Roles
            FROM admin_users u LEFT JOIN admin_user_roles ur ON ur.user_id=u.id LEFT JOIN admin_roles r ON r.id=ur.role_id
            GROUP BY u.id ORDER BY u.user_name LIMIT 100
            """,cancellationToken:ct))).AsList());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string id,string[] roles,bool disabled,CancellationToken ct)
    {
        var selected=roles.Distinct().Order().ToArray();
        if(string.IsNullOrWhiteSpace(id)||id.Length>64||selected.Length==0||selected.Except(AdminRoles.All).Any())return BadRequest();
        if(id==User.FindFirstValue(ClaimTypes.NameIdentifier)&&(disabled||!selected.Contains(AdminRoles.PlatformAdmin)))return BadRequest("Não é permitido remover seu próprio acesso global.");
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await db.QueryAsync<string>(new CommandDefinition("SELECT id FROM admin_users ORDER BY id FOR UPDATE",transaction:tx,cancellationToken:ct));
        if(await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM admin_users WHERE id=@id",new{id},tx,cancellationToken:ct))!=1)return NotFound();
        await db.ExecuteAsync(new CommandDefinition("UPDATE admin_users SET disabled=@disabled,is_disabled=@disabled WHERE id=@id",new{id,disabled},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM admin_user_roles WHERE user_id=@id",new{id},tx,cancellationToken:ct));
        foreach(var role in selected)await db.ExecuteAsync(new CommandDefinition("INSERT INTO admin_user_roles(user_id,role_id) SELECT @id,id FROM admin_roles WHERE name=@role",new{id,role},tx,cancellationToken:ct));
        var admins=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM admin_users u JOIN admin_user_roles ur ON ur.user_id=u.id JOIN admin_roles r ON r.id=ur.role_id WHERE r.name='platform_admin' AND u.disabled=FALSE AND u.is_disabled=FALSE",transaction:tx,cancellationToken:ct));
        if(admins==0)return BadRequest("É necessário manter ao menos um administrador global ativo.");
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,'admin.access_update','admin_user',@metadata,UTC_TIMESTAMP(6))",new{actor=User.Identity!.Name,metadata=JsonSerializer.Serialize(new{id,roles=selected,disabled})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);TempData["Flash"]="Acesso atualizado com auditoria. Sessões antigas serão revalidadas.";return RedirectToAction(nameof(Index));
    }
}
public sealed class AdminAccessRow
{
    public string Id { get; set; }="";
    public string UserName { get; set; }="";
    public string? Roles { get; set; }
    public bool Disabled { get; set; }
    public bool Mfa { get; set; }
    public bool MustChangePassword { get; set; }
}
