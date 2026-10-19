using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
namespace BelemRestaurantes.Worker;
public static class OperationEvidence
{
    public static async Task PrintAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var heartbeats=(await db.QueryAsync("SELECT instance_id,heartbeat_at,state,working_set_bytes,discovery_enabled,crawler_enabled FROM worker_heartbeats ORDER BY heartbeat_at DESC LIMIT 5")).ToArray();
        var users=(await db.QueryAsync("SELECT u.id,u.user_name,u.must_change_password,u.two_factor_enabled,u.access_failed_count,u.lockout_end_at,u.is_disabled,u.disabled,u.last_totp_counter,LENGTH(u.password_hash) as hash_len,LENGTH(u.two_factor_secret) as secret_len,u.password_hash,u.two_factor_secret,GROUP_CONCAT(r.name ORDER BY r.name) roles FROM admin_users u JOIN admin_user_roles ur ON ur.user_id=u.id JOIN admin_roles r ON r.id=ur.role_id WHERE u.user_name IN ('superadmin.mnsoft','operador.teste','editor.teste') GROUP BY u.id")).ToArray();
        var dpapiPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MNSOFT","BelemRestaurantes","homologacao.credentials.dpapi");
        var dpapiCreds=OperatingSystem.IsWindows()&&File.Exists(dpapiPath)?JsonSerializer.Deserialize<Dictionary<string,string>>(System.Security.Cryptography.ProtectedData.Unprotect(await File.ReadAllBytesAsync(dpapiPath),null,System.Security.Cryptography.DataProtectionScope.CurrentUser))!:new Dictionary<string,string>();
        var passVerifier=scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var userCheck=users.Select(u => {
            string uname=u.user_name;
            string phash=u.password_hash;
            byte[]? sec=u.two_factor_secret;
            bool dpapiMatch=dpapiCreds.TryGetValue(uname,out var p)&&passVerifier.Verify(p,phash);
            return new { uname, dpapiMatch, hasSecret=sec!=null&&sec.Length>0 };
        }).ToArray();
        var auditRecent=(await db.QueryAsync("SELECT id,actor_subject,action,entity_type,metadata,occurred_at FROM audit_events WHERE id >= 700 ORDER BY id ASC LIMIT 10")).ToArray();
        var counts=(await db.QueryAsync("SELECT status,COUNT(*) total FROM restaurants GROUP BY status")).ToArray();
        var targets=(await db.QueryAsync("SELECT enabled,COUNT(*) total,MIN(next_run_at) next_execution FROM discovery_targets GROUP BY enabled")).ToArray();
        var coverage=await db.QuerySingleAsync("SELECT COUNT(DISTINCT normalized_city) municipalities,COUNT(*) records FROM restaurants");
        var recipes=(await db.QueryAsync("SELECT slug,title,status,version,total_minutes FROM para_recipes ORDER BY id")).ToArray();
        var maintenance=(await db.QueryAsync("SELECT id,kind,state,attempts,completed_at,result_json,last_error FROM operational_maintenance_jobs ORDER BY id DESC LIMIT 8")).ToArray();
        var publications=(await db.QueryAsync("SELECT id,name,city,address,created_source_url FROM restaurants WHERE status='published' ORDER BY id LIMIT 20")).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,heartbeats,users,userCheck,auditRecent,counts,coverage,targets,recipes,maintenance,publications},new JsonSerializerOptions{WriteIndented=true}));
    }
}
