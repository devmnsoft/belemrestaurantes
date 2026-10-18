using System.Text.Json;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Worker;

/// <summary>Diagnóstico somente leitura: números reais, sem credenciais ou dados pessoais.</summary>
internal static class DiscoveryEvidence
{
    public static async Task PrintAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await using var connection = await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var catalog = await connection.QueryAsync("""
            SELECT status, COUNT(*) AS total FROM restaurants GROUP BY status
            """);
        var cycles = await connection.QueryAsync("""
            SELECT id, instance_id, started_at, completed_at, jobs_claimed, places_discovered,
                   restaurants_created, restaurants_updated, jobs_failed, places_skipped
            FROM discovery_cycle_runs ORDER BY id DESC LIMIT 10
            """);
        var targets = await connection.QueryAsync("""
            SELECT cycle_id, city, category, source, found_count, created_count, updated_count, skipped_count, failed_count
            FROM discovery_target_runs ORDER BY id DESC LIMIT 20
            """);
        var crawler = await connection.QueryAsync("""
            SELECT * FROM crawler_runs ORDER BY id DESC LIMIT 10
            """);
        var categories = await connection.QueryAsync("SELECT category, status, COUNT(*) AS total FROM restaurants GROUP BY category, status ORDER BY total DESC, category");
        var cities = await connection.QueryAsync("SELECT city, status, COUNT(*) AS total FROM restaurants GROUP BY city, status ORDER BY total DESC, city");
        var imports = await connection.QueryAsync("""
            SELECT c.id, c.started_at, c.completed_at, t.source, COUNT(DISTINCT t.city) AS municipalities,
                   SUM(t.found_count) AS found_count, SUM(t.created_count) AS created_count,
                   SUM(t.updated_count) AS updated_count, SUM(t.skipped_count) AS skipped_count, SUM(t.failed_count) AS failed_count
            FROM discovery_cycle_runs c JOIN discovery_target_runs t ON t.cycle_id=c.id
            WHERE t.job_id IS NULL GROUP BY c.id,c.started_at,c.completed_at,t.source ORDER BY c.id DESC LIMIT 10
            """);
        Console.WriteLine(JsonSerializer.Serialize(new { capturedAtUtc = DateTimeOffset.UtcNow, catalog, categories, cities, imports, cycles, targets, crawler },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
