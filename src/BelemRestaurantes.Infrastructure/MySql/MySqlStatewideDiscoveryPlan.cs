using System.Text.Json;
using BelemRestaurantes.Domain.Data;
using Dapper;
namespace BelemRestaurantes.Infrastructure.MySql;
public sealed class MySqlStatewideDiscoveryPlan(MySqlConnectionFactory factory)
{
    public async Task<int> ConfigureAsync(string actor,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var index=0;
        foreach(var city in ParaOfficialCatalog.Municipalities.OrderByDescending(c=>c.Name=="Belém").ThenByDescending(c=>c.IsTouristPole).ThenBy(c=>c.Name))
        {
            await db.ExecuteAsync(new CommandDefinition("""
              INSERT INTO discovery_targets(city,normalized_city,category,provider,priority,refresh_minutes,result_limit,enabled,next_run_at)
              VALUES(@Name,@NormalizedName,'regional','openstreetmap',@priority,10080,2000,TRUE,DATE_ADD(UTC_TIMESTAMP(6),INTERVAL @delay MINUTE))
              ON DUPLICATE KEY UPDATE enabled=TRUE,provider='openstreetmap',refresh_minutes=GREATEST(refresh_minutes,10080)
              """,new{city.Name,city.NormalizedName,priority=city.IsTouristPole?3:1,delay=index++*2},tx,cancellationToken:ct));
        }
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES(@actor,'discovery.statewide_plan','discovery',@metadata,UTC_TIMESTAMP(6))",new{actor,metadata=JsonSerializer.Serialize(new{municipalities=144,provider="openstreetmap",refreshMinutes=10080,firstPassSpacingMinutes=2})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return index;
    }
}
