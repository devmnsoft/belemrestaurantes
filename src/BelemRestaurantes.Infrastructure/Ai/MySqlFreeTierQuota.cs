using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Ai;

public sealed class MySqlFreeTierQuota(MySqlConnectionFactory factory) : IFreeTierQuota
{
    public Task<bool> TryReserveAsync(string service, int dailyLimit, int monthlyLimit, CancellationToken ct)
        => TryReserveUnitsAsync(service, dailyLimit, monthlyLimit, 1, ct);

    public async Task<bool> TryReserveUnitsAsync(string service, int dailyLimit, int monthlyLimit, int units, CancellationToken ct)
    {
        if (units <= 0 || units > dailyLimit || units > monthlyLimit) return false;
        if (string.IsNullOrWhiteSpace(service) || service.Length > 64) throw new ArgumentException("Invalid quota service.", nameof(service));
        await using var db = await factory.OpenAsync(ct);
        // Bounded cleanup: anonymous expired sessions are not retained indefinitely.
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM external_free_usage WHERE service LIKE 'cuia!_session!_%' ESCAPE '!' AND last_used_at < DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 DAY) LIMIT 100", cancellationToken: ct));
        await using var tx = await db.BeginTransactionAsync(ct);
        // UTC periods come from the same database clock. Always lock month before day.
        // Session counters use lifetime buckets: midnight/month rollover must not renew a live session.
        var periods = service.StartsWith("cuia_session_", StringComparison.Ordinal)
            ? new Periods("00000000", "00000001")
            : await db.QuerySingleAsync<Periods>(new CommandDefinition(
            "SELECT DATE_FORMAT(UTC_DATE(),'%Y%m00') AS Month, DATE_FORMAT(UTC_DATE(),'%Y%m%d') AS Day",
            transaction: tx, cancellationToken: ct));
        foreach (var (period, limit) in new[] { (periods.Month, monthlyLimit), (periods.Day, dailyLimit) })
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO external_free_usage(service,period) VALUES(@service,@period) ON DUPLICATE KEY UPDATE requests=requests",
                new { service, period }, tx, cancellationToken: ct));
            var changed = await db.ExecuteAsync(new CommandDefinition(
                "UPDATE external_free_usage SET requests=requests+@units,last_used_at=UTC_TIMESTAMP(6) WHERE service=@service AND period=@period AND requests<=@limit-@units",
                new { service, period, limit, units }, tx, cancellationToken: ct));
            if (changed != 1) return false; // Disposal rolls both reservations back.
        }
        await tx.CommitAsync(ct);
        return true;
    }

    private sealed record Periods(string Month, string Day);
}
