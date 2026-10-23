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
public sealed class Sprint59GeocodingAdminTests
{
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

    /// <summary>Snapshot da linha anterior, quando existia, para restaurar na limpeza.</summary>
    private sealed class TaskSnapshot
    {
        public string State { get; set; } = "";
        public int Attempts { get; set; }
        public DateTime NextRunAt { get; set; }
        public DateTime? LeaseUntil { get; set; }
        public string? LastStatus { get; set; }
        public string? ResultPrecision { get; set; }
        public long? OsmPlaceId { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private static async Task<(long Id, TaskSnapshot? Previous)> SeedTerminalTaskAsync(MySqlConnectionFactory factory, bool stale, long exclude = 0, CancellationToken ct = default)
    {
        await using var db = await factory.OpenAsync(ct);
        var fresh = await db.QueryFirstOrDefaultAsync<long?>(new CommandDefinition("""
            SELECT CAST(r.id AS SIGNED) FROM restaurants r
            WHERE r.status='published' AND r.latitude IS NULL AND CHAR_LENGTH(COALESCE(r.address,''))>=8 AND r.id<>@exclude
              AND NOT EXISTS(SELECT 1 FROM geocoding_tasks g WHERE g.restaurant_id=r.id)
            ORDER BY r.id LIMIT 1
            """, new { exclude }, cancellationToken: ct));
        var id = fresh ?? await db.QueryFirstAsync<long>(new CommandDefinition("""
            SELECT CAST(r.id AS SIGNED) FROM restaurants r
            WHERE r.status='published' AND r.latitude IS NULL AND r.id<>@exclude
            ORDER BY r.id LIMIT 1
            """, new { exclude }, cancellationToken: ct));
        TaskSnapshot? previous = null;
        var existing = await db.QueryFirstOrDefaultAsync<TaskSnapshot>(new CommandDefinition(
            "SELECT state AS State, attempts AS Attempts, next_run_at AS NextRunAt, lease_until AS LeaseUntil, last_status AS LastStatus, result_precision AS ResultPrecision, CAST(osm_place_id AS SIGNED) AS OsmPlaceId, updated_at AS UpdatedAt FROM geocoding_tasks WHERE restaurant_id=@id",
            new { id }, cancellationToken: ct));
        if (existing is not null) previous = existing;
        var whenSql = stale ? "DATE_SUB(UTC_TIMESTAMP(), INTERVAL 8 DAY)" : "UTC_TIMESTAMP(6)";
        await db.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO geocoding_tasks(restaurant_id,state,last_status,next_run_at,updated_at)
            VALUES(@id,'skipped','no_candidates',UTC_TIMESTAMP(6),{whenSql})
            ON DUPLICATE KEY UPDATE state='skipped', attempts=0, lease_until=NULL, last_status='no_candidates',
                result_precision=NULL, osm_place_id=NULL, next_run_at=UTC_TIMESTAMP(6), updated_at={whenSql}
            """, new { id }, cancellationToken: ct));
        return (id, previous);
    }

    private static async Task RestoreTaskAsync(MySqlConnectionFactory factory, long id, TaskSnapshot? previous)
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

    [Fact]
    public async Task ModeratorSeesQueueRequeuesTerminalTaskOnceAndIsAudited()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var pageStart = await AuditCursorAsync(dbFactory);
        string userId = ""; long task = 0; TaskSnapshot? previous = null;
        try
        {
            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                var page = await client.GetStringAsync("/admin/geocodificacao");
                Assert.Contains("Geocodificação autônoma", page);
                Assert.Contains("Pendente na fila", page);

                (task, previous) = await SeedTerminalTaskAsync(dbFactory, stale: false);
                await using (var db = await dbFactory.OpenAsync(default))
                {
                    var rest = await db.QueryFirstAsync("SELECT name FROM restaurants WHERE id=@id", new { id = task });
                    var filtered = await client.GetStringAsync("/admin/geocodificacao?q=" + Uri.EscapeDataString(rest.name));
                    Assert.Contains($"/admin/geocodificacao/{task}/reenfileirar", filtered);
                    var token = Token(filtered);

                    var post = await client.PostAsync($"/admin/geocodificacao/{task}/reenfileirar", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
                    Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
                    var state = await db.QueryFirstAsync("SELECT state,attempts FROM geocoding_tasks WHERE restaurant_id=@id", new { id = task });
                    Assert.Equal(("pending", 0), ((string)state.state, (int)state.attempts));
                    Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='geocoding.requeue' AND entity_id=@id", new { id = task }));

                    // Em pendente a ação volta a recusar sem duplicar auditoria.
                    var again = await client.GetStringAsync("/admin/geocodificacao?q=" + Uri.EscapeDataString(rest.name));
                    var post2 = await client.PostAsync($"/admin/geocodificacao/{task}/reenfileirar", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(again) }));
                    Assert.Equal(HttpStatusCode.Redirect, post2.StatusCode);
                    Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='geocoding.requeue' AND entity_id=@id", new { id = task }));
                }
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            await RestoreTaskAsync(dbFactory, task, previous);
            if (task > 0) await DeleteAuditsAsync(dbFactory, pageStart);
        }
    }

    [Fact]
    public async Task BulkRequeueOnlyReturnsTerminalsOlderThanSevenDays()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var pageStart = await AuditCursorAsync(dbFactory);
        string userId = ""; long oldTask = 0, recentTask = 0; TaskSnapshot? oldPrev = null, recentPrev = null;
        try
        {
            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                (oldTask, oldPrev) = await SeedTerminalTaskAsync(dbFactory, stale: true);
                (recentTask, recentPrev) = await SeedTerminalTaskAsync(dbFactory, stale: false, exclude: oldTask);
                Assert.NotEqual(oldTask, recentTask);
                var token = Token(await client.GetStringAsync("/admin/geocodificacao"));
                var post = await client.PostAsync("/admin/geocodificacao/reenfileirar-antigas", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
                Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
                await using var db = await dbFactory.OpenAsync(default);
                Assert.Equal("pending", await db.ExecuteScalarAsync<string>("SELECT state FROM geocoding_tasks WHERE restaurant_id=@id", new { id = oldTask }));
                Assert.Equal("skipped", await db.ExecuteScalarAsync<string>("SELECT state FROM geocoding_tasks WHERE restaurant_id=@id", new { id = recentTask }));
                Assert.True(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='geocoding.requeue_stale'", new { }) >= 1);
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            await RestoreTaskAsync(dbFactory, oldTask, oldPrev);
            await RestoreTaskAsync(dbFactory, recentTask, recentPrev);
            await DeleteAuditsAsync(dbFactory, pageStart);
        }
    }

    [Fact]
    public async Task NonModeratorDoesNotReachThePanel()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        string userId = "";
        try
        {
            (var client, userId) = await LoginAsync(factory, AdminRoles.Advertiser);
            using (client)
            {
                var response = await client.GetAsync("/admin/geocodificacao");
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Forbidden });
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
        }
    }

    private static async Task<long> AuditCursorAsync(MySqlConnectionFactory factory)
    {
        await using var db = await factory.OpenAsync(default);
        return await db.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(id),0) FROM audit_events");
    }

    private static async Task DeleteAuditsAsync(MySqlConnectionFactory factory, long cursor)
    {
        await using var db = await factory.OpenAsync(default);
        await db.ExecuteAsync("DELETE FROM audit_events WHERE id>@cursor AND action LIKE 'geocoding.%'", new { cursor });
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
