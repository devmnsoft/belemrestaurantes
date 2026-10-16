using BelemRestaurantes.Web.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace BelemRestaurantes.Web.Services;
public sealed class DatabaseHealthCheck(DbFactory db):IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default){
        try {
            await using var c=db.Open();
            await c.OpenAsync(cancellationToken);
            await using var command=c.CreateCommand();
            command.CommandText="SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL reachable");
        }catch(Exception){return HealthCheckResult.Unhealthy("Database unavailable");}
    }
}