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
        Console.WriteLine(JsonSerializer.Serialize(new { capturedAtUtc = DateTimeOffset.UtcNow, catalog, cycles, targets, crawler },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
