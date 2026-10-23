using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;

/// <summary>Sprint 69 — última idade de dados observada por espelho Overpass, persistida para o painel.
/// Guarda apenas o fato (o <c>osm3s.timestamp_osm_base</c> da resposta real e quando foi vista); a
/// interpretação "antigo/fresco" fica com quem lê, pela regra de 14 dias.</summary>
public sealed class MySqlOverpassMirrorFreshnessStore(MySqlConnectionFactory factory) : IOverpassMirrorFreshnessStore
{
    public async Task RecordAsync(string endpoint, DateTimeOffset? baseTimestampUtc, DateTimeOffset observedAtUtc, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO overpass_mirror_freshness(endpoint, base_timestamp, observed_at, created_at)
            VALUES(@endpoint, @baseTs, @observed, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE base_timestamp=@baseTs, observed_at=@observed
            """, new { endpoint, baseTs = baseTimestampUtc?.UtcDateTime, observed = observedAtUtc.UtcDateTime }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MirrorFreshnessSnapshot>> GetLatestAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = (await db.QueryAsync<FreshnessRow>(new CommandDefinition("""
            SELECT endpoint AS Endpoint, base_timestamp AS Base, observed_at AS Observed
            FROM overpass_mirror_freshness ORDER BY endpoint
            """, cancellationToken: ct))).ToArray();
        return rows
            .Select(r => new MirrorFreshnessSnapshot(r.Endpoint, r.Base?.ToUniversalTime(), r.Observed.ToUniversalTime()))
            .ToList();
    }

    private sealed class FreshnessRow
    {
        public string Endpoint { get; set; } = "";
        public DateTime? Base { get; set; }
        public DateTime Observed { get; set; }
        public FreshnessRow() { }
    }
}
