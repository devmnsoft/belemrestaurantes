using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint63SkipReasonTests
{
    private sealed class OriginalTask
    {
        public string State { get; set; } = "";
        public int Attempts { get; set; }
        public DateTime NextRunAt { get; set; }
        public DateTime? LeaseUntil { get; set; }
        public string? LastStatus { get; set; }
        public string? ResultPrecision { get; set; }
        public long? OsmPlaceId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public OriginalTask() { }
    }

    private sealed class AuditRow
    {
        public string Metadata { get; set; } = "";
        public AuditRow() { }
    }

    private static async Task<(HttpClient Client, string UserId)> LoginAsync(WebApplicationFactory<Program> factory, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var verifier = scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpProvider>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.Totp.v1");
        var id = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid() + "!Safe";
        var secret = totp.GenerateSecret();
        var record = new AdminUserRecord(id, "test-" + id, id + "@example.invalid", verifier.Hash(password), true,
            protector.Protect(Encoding.UTF8.GetBytes(secret)), 0, null, null);
        await users.CreateAsync(record, [role], default);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var login = await PostForm(client, "/admin/login", "/admin/login", new()
            { ["userName"] = record.UserName!, ["password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var code = totp.GenerateFor(DateTimeOffset.UtcNow, secret);
        var verify = await PostForm(client, "/admin/mfa/verify", "/admin/mfa/verify", new() { ["code"] = code, ["returnUrl"] = "/admin/geocodificacao" });
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        return (client, id);
    }

    private static async Task DeleteUserAsync(ServiceProvider root, string userId)
    {
        await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        await db.ExecuteAsync("DELETE FROM admin_user_roles WHERE user_id=@id;DELETE FROM admin_users WHERE id=@id", new { id = userId });
    }

    private static async Task<long> PickRestaurantAsync(MySqlConnectionFactory factory, long exclude = 0)
    {
        await using var db = await factory.OpenAsync(default);
        return await db.QueryFirstAsync<long>(new CommandDefinition("""
            SELECT CAST(r.id AS SIGNED) FROM restaurants r
            WHERE r.status='published' AND r.latitude IS NULL AND r.id<>@exclude
            ORDER BY r.id LIMIT 1
            """, new { exclude }));
    }

    private static async Task<OriginalTask?> ReadTaskAsync(IDbConnection db, long id) =>
        await db.QueryFirstOrDefaultAsync<OriginalTask>(new CommandDefinition(
            "SELECT state AS State, attempts AS Attempts, next_run_at AS NextRunAt, lease_until AS LeaseUntil, last_status AS LastStatus, result_precision AS ResultPrecision, CAST(osm_place_id AS SIGNED) AS OsmPlaceId, updated_at AS UpdatedAt FROM geocoding_tasks WHERE restaurant_id=@id",
            new { id }));

    private static async Task SeedSkippedTaskAsync(IDbConnection db, long id, string reason, int attempts) =>
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_tasks(restaurant_id,state,last_status,attempts,next_run_at,updated_at)
            VALUES(@id,'skipped',@reason,@attempts,'2020-01-01 00:00:00',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE state='skipped', last_status=@reason, attempts=@attempts, lease_until=NULL,
                result_precision=NULL, osm_place_id=NULL, next_run_at='2020-01-01 00:00:00', updated_at=UTC_TIMESTAMP(6)
            """, new { id, reason, attempts }));

    private static async Task RestoreTaskAsync(MySqlConnectionFactory factory, long id, OriginalTask? previous)
    {
        if (id <= 0) return;
        await using var db = await factory.OpenAsync(default);
        if (previous is null)
        {
            await db.ExecuteAsync(new CommandDefinition("DELETE FROM geocoding_tasks WHERE restaurant_id=@id", new { id }));
            return;
        }
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE geocoding_tasks SET state=@state, attempts=@attempts, next_run_at=@next, lease_until=@lease,
                last_status=@last, result_precision=@precision, osm_place_id=@place, updated_at=@updated
            WHERE restaurant_id=@id
            """, new { id, previous.State, attempts = previous.Attempts, next = previous.NextRunAt, lease = previous.LeaseUntil, last = previous.LastStatus, precision = previous.ResultPrecision, place = previous.OsmPlaceId, updated = previous.UpdatedAt }));
    }

    private static async Task<long> AuditCursorAsync(MySqlConnectionFactory factory)
    {
        await using var db = await factory.OpenAsync(default);
        return await db.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(id),0) FROM audit_events");
    }

    private static async Task<HttpResponseMessage> PostReasonAsync(HttpClient client, string motivo)
    {
        var html = await client.GetStringAsync("/admin/geocodificacao");
        return await client.PostAsync("/admin/geocodificacao/reenfileirar-por-motivo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["motivo"] = motivo
        }));
    }

    [Fact]
    public async Task RequeueByReasonRestoresOnlyThatReasonWithAuditAndPanelExposure()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var auditStart = await AuditCursorAsync(dbFactory);
        string userId = ""; long idA = 0, idB = 0; OriginalTask? taskA = null; OriginalTask? taskB = null;
        try
        {
            idA = await PickRestaurantAsync(dbFactory);
            idB = await PickRestaurantAsync(dbFactory, idA);
            await using var db = await dbFactory.OpenAsync(default);
            taskA = await ReadTaskAsync(db, idA);
            taskB = await ReadTaskAsync(db, idB);
            await SeedSkippedTaskAsync(db, idA, "unsupported_type", 1);
            await SeedSkippedTaskAsync(db, idB, "no_candidates", 3);

            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                var page = await client.GetStringAsync("/admin/geocodificacao");
                Assert.Contains("Motivos de rejeição nas linhas terminais", page);
                Assert.Contains("/admin/geocodificacao/reenfileirar-por-motivo", page);
                Assert.Contains("name=\"motivo\" value=\"unsupported_type\"", page);

                var post = await PostReasonAsync(client, "unsupported_type");
                Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

                var afterA = await ReadTaskAsync(db, idA);
                Assert.Equal(("pending", "unsupported_type"), (afterA!.State, afterA.LastStatus));
                Assert.Equal(0, afterA.Attempts);
                var afterB = await ReadTaskAsync(db, idB);
                Assert.Equal(("skipped", "no_candidates"), (afterB!.State, afterB.LastStatus));

                var audits = (await db.QueryAsync<AuditRow>(new CommandDefinition(
                    "SELECT metadata AS Metadata FROM audit_events WHERE action='geocoding.requeue_by_reason' AND id>@start",
                    new { start = auditStart }))).ToArray();
                Assert.Single(audits);
                Assert.Contains("unsupported_type", audits[0].Metadata);
            }

            // O resumo exposto ao painel/CLI distribui as terminais por motivo.
            using (var scope = root.CreateScope())
            {
                var queue = scope.ServiceProvider.GetRequiredService<IGeocodingQueue>();
                var summary = await queue.SummaryAsync(default);
                Assert.True(summary.SkipReasons.TryGetValue("no_candidates", out var n) && n >= 1);
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            if (idA > 0) await RestoreTaskAsync(dbFactory, idA, taskA);
            if (idB > 0) await RestoreTaskAsync(dbFactory, idB, taskB);
            if (auditStart > 0 || idA > 0)
            {
                await using var db = await dbFactory.OpenAsync(default);
                await db.ExecuteAsync(new CommandDefinition("DELETE FROM audit_events WHERE id>@start AND action LIKE 'geocoding.%'", new { start = auditStart }));
            }
        }
    }

    [Fact]
    public async Task RequeueByReasonWithUnknownOrInvalidMotivoChangesNothing()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var auditStart = await AuditCursorAsync(dbFactory);
        string userId = ""; long id = 0; OriginalTask? taskBefore = null;
        try
        {
            id = await PickRestaurantAsync(dbFactory);
            await using var db = await dbFactory.OpenAsync(default);
            taskBefore = await ReadTaskAsync(db, id);
            await SeedSkippedTaskAsync(db, id, "unsupported_type", 5);

            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                // Motivo bem formatado mas sem nenhuma linha terminal correspondente.
                var post = await PostReasonAsync(client, "zzz_sem_linha_terminal");
                Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
                var t1 = await ReadTaskAsync(db, id);
                Assert.Equal(("skipped", "unsupported_type"), (t1!.State, t1.LastStatus));
                Assert.Equal(5, t1.Attempts);

                // Motivo fora do formato aceito: nada roda, mensagem de erro.
                var post2 = await PostReasonAsync(client, "BAD-MOTIVO");
                Assert.Equal(HttpStatusCode.Redirect, post2.StatusCode);
                var t2 = await ReadTaskAsync(db, id);
                Assert.Equal(("skipped", "unsupported_type"), (t2!.State, t2.LastStatus));
                Assert.Equal(5, t2.Attempts);

                Assert.Equal(0, await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM audit_events WHERE action='geocoding.requeue_by_reason' AND id>@start", new { start = auditStart }));
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            if (id > 0) await RestoreTaskAsync(dbFactory, id, taskBefore);
            await using var db = await dbFactory.OpenAsync(default);
            await db.ExecuteAsync(new CommandDefinition("DELETE FROM audit_events WHERE id>@start AND action LIKE 'geocoding.%'", new { start = auditStart }));
        }
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string page, string action, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        fields["__RequestVerificationToken"] = Token(html);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields));
    }
}
