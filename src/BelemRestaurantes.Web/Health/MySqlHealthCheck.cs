using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BelemRestaurantes.Web.Health;

/// <summary>Health check de conexão MySQL (substitui referência herdada a PostgreSQL).</summary>
public sealed class MySqlHealthCheck(MySqlConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = factory.Create();
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("MySQL reachable", new Dictionary<string, object> { ["database"] = factory.Database });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MySQL unavailable", ex);
        }
    }
}