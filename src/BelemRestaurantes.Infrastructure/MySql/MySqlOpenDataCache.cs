using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlOpenDataCache(MySqlConnectionFactory factory) : IOpenDataCache
{
    public async Task<OpenDataSnapshot?> GetAsync(string city, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<SnapshotRow>(new CommandDefinition(
            "SELECT city, payload, fetched_at AS FetchedAt FROM open_data_snapshots WHERE city=@city", new { city }, cancellationToken: ct));
        return row is null ? null : new(row.City, row.Payload, row.FetchedAt);
    }

    public async Task SaveAsync(OpenDataSnapshot snapshot, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO open_data_snapshots(city,payload,fetched_at) VALUES(@City,@Payload,@FetchedAt)
            ON DUPLICATE KEY UPDATE payload=VALUES(payload), fetched_at=VALUES(fetched_at)
            """, snapshot, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OpenDataSnapshot>> ListAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SnapshotRow>(new CommandDefinition(
            "SELECT city,payload,fetched_at AS FetchedAt FROM open_data_snapshots ORDER BY city", cancellationToken: ct));
        return rows.Select(r => new OpenDataSnapshot(r.City, r.Payload, r.FetchedAt)).ToArray();
    }

    private sealed class SnapshotRow
    {
        public string City { get; set; } = "";
        public string Payload { get; set; } = "";
        public DateTimeOffset FetchedAt { get; set; }
    }
}
