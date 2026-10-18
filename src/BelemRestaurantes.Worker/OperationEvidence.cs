using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.Worker;
public static class OperationEvidence
{
    public static async Task PrintAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var heartbeats=(await db.QueryAsync("SELECT instance_id,heartbeat_at,state,working_set_bytes,discovery_enabled,crawler_enabled FROM worker_heartbeats ORDER BY heartbeat_at DESC LIMIT 5")).ToArray();
        var users=(await db.QueryAsync("SELECT u.user_name,u.must_change_password,u.two_factor_enabled,GROUP_CONCAT(r.name ORDER BY r.name) roles FROM admin_users u JOIN admin_user_roles ur ON ur.user_id=u.id JOIN admin_roles r ON r.id=ur.role_id WHERE u.user_name IN ('superadmin.mnsoft','operador.teste','editor.teste') GROUP BY u.id")).ToArray();
        var counts=(await db.QueryAsync("SELECT status,COUNT(*) total FROM restaurants GROUP BY status")).ToArray();
        var targets=(await db.QueryAsync("SELECT enabled,COUNT(*) total,MIN(next_run_at) next_execution FROM discovery_targets GROUP BY enabled")).ToArray();
        var coverage=await db.QuerySingleAsync("SELECT COUNT(DISTINCT normalized_city) municipalities,COUNT(*) records FROM restaurants");
        var recipes=(await db.QueryAsync("SELECT slug,title,status,version,total_minutes FROM para_recipes ORDER BY id")).ToArray();
        var maintenance=(await db.QueryAsync("SELECT id,kind,state,attempts,completed_at,result_json,last_error FROM operational_maintenance_jobs ORDER BY id DESC LIMIT 8")).ToArray();
        var publications=(await db.QueryAsync("SELECT id,name,city,address,created_source_url FROM restaurants WHERE status='published' ORDER BY id LIMIT 20")).ToArray();
        var recentMfa=(await db.QueryAsync("SELECT metadata,occurred_at FROM audit_events WHERE action='mfa_enrol_rejected' ORDER BY id DESC LIMIT 3")).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,heartbeats,users,counts,coverage,targets,recipes,maintenance,publications,recentMfa},new JsonSerializerOptions{WriteIndented=true}));
    }
}
