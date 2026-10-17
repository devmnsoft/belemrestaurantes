using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Members;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class EmailOutboxTests
{
    [Fact]
    public async Task Expired_lease_recovers_send_erases_payload_and_failures_reach_dead_letter_without_secrets()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build(); await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var factory = provider.GetRequiredService<MySqlConnectionFactory>(); var protector = provider.GetRequiredService<MailPayloadProtector>();
        var clock = provider.GetRequiredService<IClock>(); var sender = new RecordingSender();
        var dispatcher = new MySqlEmailDispatcher(factory, protector, sender, clock);
        await using var connection = await factory.OpenAsync(default);
        var id = Guid.NewGuid().ToString("N"); var failing = Guid.NewGuid().ToString("N");
        var payload = protector.Protect(new("outbox@example.invalid", "Test", "confirm", "https://example.invalid/#token=synthetic", "en"));
        await connection.ExecuteAsync("""
            INSERT INTO transactional_email_outbox(id,payload,state,next_attempt_at,created_at,expires_at,lease_id,locked_until)
            VALUES(@id,@payload,'sending',DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 DAY),UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 1 HOUR),'stale',DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE))
            """, new { id, payload });
        Assert.Equal(1, await dispatcher.DispatchAsync(1, default)); Assert.Contains(id, sender.Ids);
        Assert.Equal("sent", await connection.ExecuteScalarAsync<string>("SELECT state FROM transactional_email_outbox WHERE id=@id", new { id }));
        Assert.Null(await connection.ExecuteScalarAsync<byte[]?>("SELECT payload FROM transactional_email_outbox WHERE id=@id", new { id }));
        await connection.ExecuteAsync("INSERT INTO transactional_email_outbox(id,payload,next_attempt_at,created_at,expires_at) VALUES(@id,@payload,DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 DAY),UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 1 HOUR))", new { id = failing, payload });
        sender.Fail = true;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await connection.ExecuteAsync("UPDATE transactional_email_outbox SET next_attempt_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 2 DAY) WHERE id=@id", new { id = failing });
            await dispatcher.DispatchAsync(1, default);
        }
        var row = await connection.QuerySingleAsync<MailState>("SELECT state,last_error,attempts,payload FROM transactional_email_outbox WHERE id=@id", new { id = failing });
        Assert.Equal("dead_lettered", row.state); Assert.Equal(6, row.attempts); Assert.Null(row.payload); Assert.Equal("InvalidOperationException", row.last_error);
    }

    private sealed class RecordingSender : ITransactionalMailSender
    {
        public List<string> Ids { get; } = []; public bool Fail { get; set; }
        public Task SendAsync(string id, TransactionalMail message, CancellationToken ct)
        { Ids.Add(id); if (Fail) throw new InvalidOperationException("synthetic-secret-must-not-be-logged"); return Task.CompletedTask; }
    }
    private sealed class MailState
    { public string state { get; set; } = ""; public string? last_error { get; set; } public int attempts { get; set; } public byte[]? payload { get; set; } }
}
