using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Migrations;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class AdminLoginTests
{
    [Theory]
    [InlineData(AdminRoles.PlatformAdmin, true)]
    [InlineData(AdminRoles.Advertiser, false)]
    public async Task Password_then_totp_respects_roles_replay_fails_and_password_change_revokes_cookie(string role, bool canModerate)
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
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
        try
        {
            var login = await PostForm(client, "/admin/login", "/admin/login", new()
                { ["userName"] = record.UserName, ["password"] = password });
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var codeInstant = DateTimeOffset.UtcNow;
            var code = totp.GenerateFor(codeInstant, secret);
            var verify = await PostForm(client, "/admin/mfa/verify", "/admin/mfa/verify", new() { ["code"] = code });
            Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
            Assert.Contains(verify.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".BelRest.AdminAuth="));
            var protectedResponse = await client.GetAsync("/api/admin/review/pending");
            if (canModerate) Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
            else Assert.Contains(protectedResponse.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Forbidden });

            // A successful code cannot be consumed again, including concurrent requests.
            var counter = codeInstant.ToUnixTimeSeconds() / 30;
            Assert.False(await users.ConsumeTotpAsync(id, counter, default));
            var next = await Task.WhenAll(users.ConsumeTotpAsync(id, counter + 10, default), users.ConsumeTotpAsync(id, counter + 10, default));
            Assert.Single(next, value => value);

            await using var connection = await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default);
            await connection.ExecuteAsync("UPDATE admin_users SET password_hash = @hash WHERE id = @id", new { hash = verifier.Hash(password + "changed"), id });
            var revoked = await client.GetAsync("/api/admin/review/pending");
            Assert.Contains(revoked.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
        }
        finally
        {
            await using var connection = await scope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory>().OpenAsync(default);
            await connection.ExecuteAsync("DELETE FROM admin_user_roles WHERE user_id = @id", new { id });
            await connection.ExecuteAsync("DELETE FROM admin_users WHERE id = @id", new { id });
        }
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string page, string action, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields));
    }
}
