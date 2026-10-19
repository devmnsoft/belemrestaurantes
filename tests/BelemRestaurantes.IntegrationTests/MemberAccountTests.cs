using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Members;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class MemberAccountTests
{
    [Fact]
    public async Task Registration_confirmation_login_recovery_and_revocation_use_mysql_and_private_single_use_tokens()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var accounts = provider.GetRequiredService<IMemberAccounts>();
        var protector = provider.GetRequiredService<MailPayloadProtector>();
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        var password = "Test!" + Guid.NewGuid().ToString("N") + "1";
        await accounts.RegisterAsync(new("Test Member", email, password, true, "ja"), default);
        var id = await connection.ExecuteScalarAsync<string>("SELECT id FROM member_users WHERE email=@email", new { email });
        Assert.NotNull(id);
        Assert.Null(await accounts.LoginAsync(email, password, true, default));
        var rows = (await connection.QueryAsync<OutboxRow>("SELECT id,payload FROM transactional_email_outbox WHERE state='pending'")).ToArray();
        var row = Assert.Single(rows, item => {
            try { return protector.Unprotect(item.payload).Recipient == email; }
            catch { return false; }
        });
        var message = protector.Unprotect(row.payload);
        Assert.Equal("ja", message.Culture);
        Assert.DoesNotContain(email, System.Text.Encoding.UTF8.GetString(row.payload));
        Assert.DoesNotContain("?", message.Link!);
        var token = message.Link!.Split("#token=")[1];
        Assert.Equal(MySqlMemberAccounts.HashToken(token), await connection.ExecuteScalarAsync<string>("SELECT token_hash FROM member_tokens WHERE user_id=@id", new { id }));
        var confirms = await Task.WhenAll(accounts.ConfirmEmailAsync(token, default), accounts.ConfirmEmailAsync(token, default));
        Assert.Single(confirms, result => result);
        var first = await accounts.LoginAsync(email, password, true, default); Assert.NotNull(first);
        var second = await accounts.LoginAsync(email, password, false, default); Assert.NotNull(second);
        Assert.NotNull(await accounts.ValidateSessionAsync(first.SessionToken, default));
        await accounts.RevokeOtherSessionsAsync(first.UserId, first.SessionToken, default);
        Assert.Null(await accounts.ValidateSessionAsync(second.SessionToken, default));
        Assert.NotNull(await accounts.ValidateSessionAsync(first.SessionToken, default));
        var mailCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM transactional_email_outbox");
        await accounts.RegisterAsync(new("Duplicate", email.ToUpperInvariant(), password, true, "en"), default);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_users WHERE normalized_email=@normalized", new { normalized = email.ToUpperInvariant() }));
        Assert.Equal(mailCount, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM transactional_email_outbox"));
        await accounts.RequestTokenAsync(email, "reset", "fr", default);
        var resetRows = await connection.QueryAsync<OutboxRow>("SELECT id,payload FROM transactional_email_outbox WHERE payload IS NOT NULL");
        var resetRow = Assert.Single(resetRows, item => {
            try { return protector.Unprotect(item.payload) is { Kind: "reset" } mail && mail.Recipient == email; }
            catch { return false; }
        });
        var resetToken = protector.Unprotect(resetRow.payload).Link!.Split("#token=")[1];
        var beforeResend = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_tokens WHERE user_id=@id", new { id });
        await accounts.RequestTokenAsync(email, "reset", "fr", default);
        Assert.Equal(beforeResend, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_tokens WHERE user_id=@id", new { id }));
        Assert.True(await accounts.ResetPasswordAsync(resetToken, password + "New!", default));
        Assert.False(await accounts.ResetPasswordAsync(resetToken, password + "Again!", default));
        Assert.Null(await accounts.ValidateSessionAsync(first.SessionToken, default));
        var fresh = await accounts.LoginAsync(email, password + "New!", true, default); Assert.NotNull(fresh);
        await accounts.RevokeSessionAsync(id, fresh.SessionToken, default);
        Assert.Null(await accounts.ValidateSessionAsync(fresh.SessionToken, default));
        Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE actor_subject=@id", new { id }) >= 5);
    }

    [Fact]
    public async Task Public_journey_confirms_email_and_member_cookie_never_authorizes_admin()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString(), ["MailEncryption:KeyRingPath"] = DatabaseEnvironment.MailKeys })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        var password = "Journey1!" + Guid.NewGuid().ToString("N");
        var response = await Post(client, "/account/register", new() { ["FullName"] = "Public Member", ["Email"] = email, ["Password"] = password, ["ConfirmPassword"] = password, ["AcceptTerms"] = "true" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var protector = provider.GetRequiredService<MailPayloadProtector>();
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var rows = await connection.QueryAsync<OutboxRow>("SELECT id,payload FROM transactional_email_outbox WHERE payload IS NOT NULL");
        var confirmation = protector.Unprotect(Assert.Single(rows, row => {
            try { return protector.Unprotect(row.payload).Recipient == email; }
            catch { return false; }
        }).payload).Link!.Split("#token=")[1];
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/account/confirm", new() { ["token"] = confirmation })).StatusCode);
        var login = await Post(client, "/account/login", new() { ["Email"] = email, ["Password"] = password, ["Remember"] = "true", ["ReturnUrl"] = "https://example.invalid" });
        Assert.Equal("/account", login.Headers.Location?.ToString());
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".BelRest.Member="));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account")).StatusCode);
        Assert.Contains((await client.GetAsync("/api/admin/review/pending")).StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/account/logout", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/account")).StatusCode);
    }

    [Fact]
    public async Task Expired_token_and_unaccepted_terms_are_rejected()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build(); await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var accounts = provider.GetRequiredService<IMemberAccounts>();
        var email = Guid.NewGuid() + "@example.invalid"; var password = "Valid1!" + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.RegisterAsync(new("Test", email, password, false, "en"), default));
        await accounts.RegisterAsync(new("Test", email, password, true, "en"), default);
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var protector = provider.GetRequiredService<MailPayloadProtector>();
        var rows = await connection.QueryAsync<OutboxRow>("SELECT id,payload FROM transactional_email_outbox WHERE payload IS NOT NULL");
        var token = protector.Unprotect(Assert.Single(rows, row => {
            try { return protector.Unprotect(row.payload).Recipient == email; }
            catch { return false; }
        }).payload).Link!.Split("#token=")[1];
        await connection.ExecuteAsync("UPDATE member_tokens SET expires_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE) WHERE token_hash=@hash", new { hash = MySqlMemberAccounts.HashToken(token) });
        Assert.False(await accounts.ConfirmEmailAsync(token, default));
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(path == "/account/logout" ? "/account" : path);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(token.Success);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    private sealed class OutboxRow { public string id { get; set; } = ""; public byte[] payload { get; set; } = []; }
}
