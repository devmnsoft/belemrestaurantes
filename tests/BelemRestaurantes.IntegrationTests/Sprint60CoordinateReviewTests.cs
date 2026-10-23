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
public sealed class Sprint60CoordinateReviewTests
{
    private sealed class OriginalCoordinate
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Precisao { get; set; }
        public string? Source { get; set; }
        public OriginalCoordinate() { }
    }

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

    private sealed class HistoryRow
    {
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
        public string SourceType { get; set; } = "";
        public HistoryRow() { }
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

    private static async Task<OriginalCoordinate> ReadCoordinateAsync(IDbConnection db, long id) =>
        await db.QueryFirstAsync<OriginalCoordinate>(new CommandDefinition(
            "SELECT CAST(latitude AS DOUBLE) AS Latitude, CAST(longitude AS DOUBLE) AS Longitude, coordinate_precision AS Precisao, coordinate_source AS Source FROM restaurants WHERE id=@id",
            new { id }));

    private static async Task SetCoordinateAsync(IDbConnection db, long id, double? lat, double? lng, string? precision, string? source) =>
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE restaurants SET latitude=@lat, longitude=@lng, coordinate_precision=@precision, coordinate_source=@source WHERE id=@id",
            new { id, lat, lng, precision, source }));

    private static async Task<OriginalTask?> ReadTaskAsync(IDbConnection db, long id) =>
        await db.QueryFirstOrDefaultAsync<OriginalTask>(new CommandDefinition(
            "SELECT state AS State, attempts AS Attempts, next_run_at AS NextRunAt, lease_until AS LeaseUntil, last_status AS LastStatus, result_precision AS ResultPrecision, CAST(osm_place_id AS SIGNED) AS OsmPlaceId, updated_at AS UpdatedAt FROM geocoding_tasks WHERE restaurant_id=@id",
            new { id }));

    private static async Task SetCompletedTaskAsync(IDbConnection db, long id) =>
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO geocoding_tasks(restaurant_id,state,last_status,result_precision,osm_place_id,next_run_at,updated_at)
            VALUES(@id,'completed','rooftop','rooftop',999,'2099-12-31 23:59:59',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE state='completed', last_status='rooftop', attempts=0, lease_until=NULL,
                result_precision='rooftop', osm_place_id=999, next_run_at='2099-12-31 23:59:59', updated_at=UTC_TIMESTAMP(6)
            """, new { id }));

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

    private static async Task<long> HistoryCursorAsync(MySqlConnectionFactory factory)
    {
        await using var db = await factory.OpenAsync(default);
        return await db.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(id),0) FROM restaurant_attribute_history");
    }

    [Fact]
    public async Task ModeratorRevertsAppliedOsmCoordinateWithHistoryAndAudit()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var auditStart = await AuditCursorAsync(dbFactory);
        var historyStart = await HistoryCursorAsync(dbFactory);
        string userId = ""; long id = 0; OriginalCoordinate? original = null; OriginalTask? taskBefore = null;
        try
        {
            id = await PickRestaurantAsync(dbFactory);
            await using var db = await dbFactory.OpenAsync(default);
            original = await ReadCoordinateAsync(db, id);
            taskBefore = await ReadTaskAsync(db, id);
            await SetCoordinateAsync(db, id, -1.5, -48.5, "rooftop", "OpenStreetMap via Nominatim (objeto 999)");
            await SetCompletedTaskAsync(db, id);

            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                var rest = await db.QueryFirstAsync("SELECT name FROM restaurants WHERE id=@id", new { id });
                var page = await client.GetStringAsync("/admin/geocodificacao?q=" + Uri.EscapeDataString(rest.name));
                Assert.Contains($"/admin/geocodificacao/{id}/reverter", page);
                var post = await client.PostAsync($"/admin/geocodificacao/{id}/reverter", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(page) }));
                Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

                var after = await ReadCoordinateAsync(db, id);
                Assert.Null(after.Latitude);
                Assert.Null(after.Longitude);
                Assert.Null(after.Precisao);
                Assert.Null(after.Source);

                var task = await ReadTaskAsync(db, id);
                Assert.Equal(("skipped", "reverted_by_admin"), (task!.State, task.LastStatus));

                Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='geocoding.revert' AND entity_id=@id", new { id }));
                var hist = await db.QueryFirstAsync<HistoryRow>(new CommandDefinition(
                    "SELECT old_value AS OldValue, new_value AS NewValue, source_type AS SourceType FROM restaurant_attribute_history WHERE restaurant_id=@id AND attribute_name='coordinate' ORDER BY id DESC LIMIT 1",
                    new { id }));
                Assert.Contains("-1.5", hist.OldValue);
                Assert.Equal("admin_review", hist.SourceType);
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            if (id > 0)
            {
                await using var db = await dbFactory.OpenAsync(default);
                if (original is not null) await SetCoordinateAsync(db, id, original.Latitude, original.Longitude, original.Precisao, original.Source);
                await RestoreTaskAsync(dbFactory, id, taskBefore);
                await db.ExecuteAsync(new CommandDefinition("DELETE FROM audit_events WHERE id>@start AND action LIKE 'geocoding.%'", new { start = auditStart }));
                await db.ExecuteAsync(new CommandDefinition("DELETE FROM restaurant_attribute_history WHERE id>@start AND attribute_name='coordinate'", new { start = historyStart }));
            }
        }
    }

    [Fact]
    public async Task RevertWithoutOsmSourceChangesNothing()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        var dbFactory = root.GetRequiredService<MySqlConnectionFactory>();
        var auditStart = await AuditCursorAsync(dbFactory);
        var historyStart = await HistoryCursorAsync(dbFactory);
        string userId = ""; long id = 0; OriginalCoordinate? original = null;
        try
        {
            id = await PickRestaurantAsync(dbFactory);
            await using var db = await dbFactory.OpenAsync(default);
            original = await ReadCoordinateAsync(db, id);
            await SetCoordinateAsync(db, id, 1.0, 2.0, null, "cadastro_do_comerciante");

            (var client, userId) = await LoginAsync(factory, AdminRoles.Moderator);
            using (client)
            {
                var page = await client.GetStringAsync("/admin/geocodificacao");
                var post = await client.PostAsync($"/admin/geocodificacao/{id}/reverter", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(page) }));
                Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

                var after = await ReadCoordinateAsync(db, id);
                Assert.Equal((1.0, 2.0, "cadastro_do_comerciante"), (after.Latitude, after.Longitude, after.Source));
                Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='geocoding.revert' AND entity_id=@id", new { id }));
                Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurant_attribute_history WHERE restaurant_id=@id AND attribute_name='coordinate'", new { id }));
            }
        }
        finally
        {
            if (userId.Length > 0) await DeleteUserAsync(root, userId);
            if (id > 0)
            {
                await using var db = await dbFactory.OpenAsync(default);
                if (original is not null) await SetCoordinateAsync(db, id, original.Latitude, original.Longitude, original.Precisao, original.Source);
                await db.ExecuteAsync(new CommandDefinition("DELETE FROM audit_events WHERE id>@start AND action LIKE 'geocoding.%'", new { start = auditStart }));
                await db.ExecuteAsync(new CommandDefinition("DELETE FROM restaurant_attribute_history WHERE id>@start AND attribute_name='coordinate'", new { start = historyStart }));
            }
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
