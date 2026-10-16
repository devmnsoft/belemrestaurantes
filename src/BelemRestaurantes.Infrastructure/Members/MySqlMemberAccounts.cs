using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BelemRestaurantes.Infrastructure.Members;

public sealed class MySqlMemberAccounts(MySqlConnectionFactory factory, IPasswordVerifier passwords, IClock clock,
    IConfiguration configuration, MailPayloadProtector mailProtector) : IMemberAccounts
{
    private static readonly string DummyHash = $"600000.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))}.{Convert.ToBase64String(new byte[32])}";
    private static readonly string[] Cultures = ["pt-BR", "en", "fr", "es", "ru", "de", "ja", "zh-Hans"];
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Culture(string value) => Cultures.Contains(value) ? value : "pt-BR";
    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
    private static bool ValidEmail(string? email) => email is { Length: > 3 and <= 200 } &&
        System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address == email && !email.Any(char.IsWhiteSpace);

    public async Task RegisterAsync(RegisterMember input, CancellationToken ct)
    {
        if (!input.AcceptTerms || string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length is < 2 or > 160 ||
            !ValidEmail(input.Email) || AdminPasswordPolicy.Validate(input.Password).Count > 0)
            throw new ArgumentException("Invalid registration.");
        var hash = passwords.Hash(input.Password);
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = Guid.NewGuid().ToString("N");
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO member_users(id,full_name,email,normalized_email,password_hash,security_stamp,preferred_culture,terms_version,terms_accepted_at)
                VALUES(@id,@name,@email,@normalized,@hash,@stamp,@culture,'2026-10',@now)
                """, new { id, name = input.FullName.Trim(), email = input.Email.Trim(), normalized = Normalize(input.Email), hash,
                    stamp = Guid.NewGuid().ToString("N"), culture = Culture(input.Culture), now = clock.UtcNow }, tx, cancellationToken: ct));
        }
        catch (MySqlException ex) when (ex.Number == 1062) { await tx.RollbackAsync(ct); return; }
        var user = await connection.QuerySingleAsync<UserRow>(new CommandDefinition("SELECT * FROM member_users WHERE id=@id", new { id }, tx, cancellationToken: ct));
        await CreateTokenAsync(connection, tx, user, "confirm", Culture(input.Culture), ct);
        await AuditAsync(connection, tx, id, "member.register", ct);
        await tx.CommitAsync(ct);
    }

    public async Task<MemberSession?> LoginAsync(string email, string password, bool remember, CancellationToken ct)
    {
        if (!ValidEmail(email) || string.IsNullOrEmpty(password) || password.Length > 256) return null;
        await using var connection = await factory.OpenAsync(ct);
        // Hash verification occurs outside the row lock; stamp/hash are rechecked under lock afterwards.
        var before = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition("SELECT * FROM member_users WHERE normalized_email=@email", new { email = Normalize(email) }, cancellationToken: ct));
        var valid = passwords.Verify(password, before?.password_hash ?? DummyHash);
        if (before is null) return null;
        await using var tx = await connection.BeginTransactionAsync(ct);
        var user = await connection.QuerySingleAsync<UserRow>(new CommandDefinition("SELECT * FROM member_users WHERE id=@id FOR UPDATE", new { id = before.id }, tx, cancellationToken: ct));
        var now = clock.UtcNow;
        if (user.is_blocked || user.lockout_until > now || !user.email_confirmed) return null;
        if (!valid || user.password_hash != before.password_hash)
        {
            var failures = user.failed_attempts + 1;
            var duration = TimeSpan.FromSeconds(Math.Min(3600, failures < 5 ? 5 * failures : 900 * Math.Pow(2, Math.Min(2, (failures - 5) / 5))));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE member_users SET failed_attempts=@failures,lockout_until=@until WHERE id=@id",
                new { failures, until = now.Add(duration), id = user.id }, tx, cancellationToken: ct));
            await AuditAsync(connection, tx, user.id, "member.login_failed", ct);
            await tx.CommitAsync(ct); return null;
        }
        var token = NewToken(); var expiry = now.Add(remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
        await connection.ExecuteAsync(new CommandDefinition("UPDATE member_users SET failed_attempts=0,lockout_until=NULL WHERE id=@id", new { id = user.id }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO member_sessions(id_hash,user_id,security_stamp,created_at,expires_at) VALUES(@hash,@id,@stamp,@now,@expiry)",
            new { hash = HashToken(token), id = user.id, stamp = user.security_stamp, now, expiry }, tx, cancellationToken: ct));
        await AuditAsync(connection, tx, user.id, "member.login", ct); await tx.CommitAsync(ct);
        return Session(user, token, expiry);
    }

    public async Task<MemberSession?> ValidateSessionAsync(string token, CancellationToken ct)
    {
        if (!TokenFormat(token)) return null;
        await using var connection = await factory.OpenAsync(ct);
        var user = await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition("""
            SELECT u.id,u.full_name,u.email,u.preferred_culture,s.expires_at
            FROM member_sessions s JOIN member_users u ON u.id=s.user_id
            WHERE s.id_hash=@hash AND s.revoked_at IS NULL AND s.expires_at>@now AND s.security_stamp=u.security_stamp
            AND u.is_blocked=FALSE AND u.email_confirmed=TRUE AND (u.lockout_until IS NULL OR u.lockout_until<=@now)
            """, new { hash = HashToken(token), now = clock.UtcNow }, cancellationToken: ct));
        return user is null ? null : new(user.id,user.full_name,user.email,user.preferred_culture,token,user.expires_at);
    }

    public async Task RevokeSessionAsync(string userId, string token, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE member_sessions SET revoked_at=@now WHERE user_id=@userId AND id_hash=@hash AND revoked_at IS NULL", new { userId, hash = HashToken(token), now = clock.UtcNow }, tx, cancellationToken: ct));
        await AuditAsync(connection, tx, userId, "member.logout", ct); await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<MemberSessionView>> SessionsAsync(string userId, string currentToken, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SessionListRow>(new CommandDefinition("SELECT id_hash,created_at,expires_at FROM member_sessions WHERE user_id=@userId AND revoked_at IS NULL AND expires_at>@now ORDER BY created_at DESC LIMIT 100", new { userId, now = clock.UtcNow }, cancellationToken: ct));
        return rows.Select(row => new MemberSessionView(row.id_hash,row.created_at,row.expires_at,row.id_hash==HashToken(currentToken))).ToArray();
    }

    public async Task RevokeOtherSessionsAsync(string userId, string currentToken, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE member_sessions SET revoked_at=@now WHERE user_id=@userId AND id_hash<>@hash AND revoked_at IS NULL", new { userId, hash = HashToken(currentToken), now = clock.UtcNow }, tx, cancellationToken: ct));
        await AuditAsync(connection, tx, userId, "member.revoke_sessions", ct); await tx.CommitAsync(ct);
    }

    public async Task RequestTokenAsync(string email, string purpose, string culture, CancellationToken ct)
    {
        if (!ValidEmail(email) || purpose is not ("confirm" or "reset")) return;
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var user = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition("SELECT * FROM member_users WHERE normalized_email=@email FOR UPDATE", new { email = Normalize(email) }, tx, cancellationToken: ct));
        if (user is null || user.is_blocked || (purpose=="confirm" && user.email_confirmed)) return;
        var recent = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM member_tokens WHERE user_id=@id AND purpose=@purpose AND created_at>@since", new { id=user.id,purpose,since=clock.UtcNow.AddMinutes(-5) }, tx,cancellationToken:ct));
        if (recent > 0) return;
        await CreateTokenAsync(connection,tx,user,purpose,Culture(culture),ct); await tx.CommitAsync(ct);
    }

    public Task<bool> ConfirmEmailAsync(string token, CancellationToken ct) => ConsumeTokenAsync(token,"confirm",null,ct);
    public Task<bool> ResetPasswordAsync(string token, string password, CancellationToken ct) =>
        AdminPasswordPolicy.Validate(password).Count > 0 ? Task.FromResult(false) : ConsumeTokenAsync(token,"reset",passwords.Hash(password),ct);

    private async Task<bool> ConsumeTokenAsync(string token,string purpose,string? newHash,CancellationToken ct)
    {
        if (!TokenFormat(token)) return false;
        await using var connection=await factory.OpenAsync(ct);
        // Resolve the owner, then consistently lock user before tokens to avoid lock inversions.
        var owner=await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT user_id FROM member_tokens WHERE token_hash=@hash",new {hash=HashToken(token)},cancellationToken:ct));
        if(owner is null) return false;
        await using var tx=await connection.BeginTransactionAsync(ct);
        var user=await connection.QuerySingleAsync<UserRow>(new CommandDefinition("SELECT * FROM member_users WHERE id=@owner FOR UPDATE",new {owner},tx,cancellationToken:ct));
        if(user.is_blocked)return false;
        var consumed=await connection.ExecuteAsync(new CommandDefinition("UPDATE member_tokens SET used_at=@now WHERE token_hash=@hash AND user_id=@owner AND purpose=@purpose AND security_stamp=@stamp AND used_at IS NULL AND expires_at>@now",new {hash=HashToken(token),owner,purpose,stamp=user.security_stamp,now=clock.UtcNow},tx,cancellationToken:ct));
        if(consumed!=1)return false;
        if(purpose=="confirm")
            await connection.ExecuteAsync(new CommandDefinition("UPDATE member_users SET email_confirmed=TRUE WHERE id=@owner",new {owner},tx,cancellationToken:ct));
        else
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE member_users SET password_hash=@newHash,security_stamp=@stamp,failed_attempts=0,lockout_until=NULL WHERE id=@owner",new {owner,newHash,stamp=Guid.NewGuid().ToString("N")},tx,cancellationToken:ct));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE member_sessions SET revoked_at=@now WHERE user_id=@owner AND revoked_at IS NULL",new {owner,now=clock.UtcNow},tx,cancellationToken:ct));
            await EnqueueAsync(connection,tx,new(user.email,user.full_name,"password_changed",null,user.preferred_culture),ct);
        }
        await connection.ExecuteAsync(new CommandDefinition("UPDATE member_tokens SET used_at=@now WHERE user_id=@owner AND purpose=@purpose AND used_at IS NULL",new {owner,purpose,now=clock.UtcNow},tx,cancellationToken:ct));
        await AuditAsync(connection,tx,owner,"member."+purpose,ct);await tx.CommitAsync(ct);return true;
    }

    private async Task CreateTokenAsync(MySqlConnection connection,MySqlTransaction tx,UserRow user,string purpose,string culture,CancellationToken ct)
    {
        var origin=configuration["MemberAccounts:PublicOrigin"]??"https://belemrestaurantes.com.br";
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0)
            throw new InvalidOperationException("MemberAccounts:PublicOrigin requires an HTTPS origin.");
        var token=NewToken(); var now=clock.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("UPDATE member_tokens SET used_at=@now WHERE user_id=@id AND purpose=@purpose AND used_at IS NULL",new {id=user.id,purpose,now},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO member_tokens(token_hash,user_id,purpose,security_stamp,created_at,expires_at) VALUES(@hash,@id,@purpose,@stamp,@now,@expiry)",new {hash=HashToken(token),id=user.id,purpose,stamp=user.security_stamp,now,expiry=now.Add(purpose=="confirm"?TimeSpan.FromHours(24):TimeSpan.FromMinutes(30))},tx,cancellationToken:ct));
        // Fragment is never transmitted to the server or IIS access logs. account.js moves it into the POST form.
        var link=uri.GetLeftPart(UriPartial.Authority)+"/account/"+(purpose=="confirm"?"confirm":"reset")+"#token="+token;
        await EnqueueAsync(connection,tx,new(user.email,user.full_name,purpose,link,culture),ct);
    }

    private async Task EnqueueAsync(MySqlConnection connection,MySqlTransaction tx,TransactionalMail message,CancellationToken ct)
    {
        var now=clock.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO transactional_email_outbox(id,payload,next_attempt_at,created_at,expires_at) VALUES(@id,@payload,@now,@now,@expiry)",new {id=Guid.NewGuid().ToString("N"),payload=mailProtector.Protect(message),now,expiry=now.AddHours(message.Kind=="reset"?0.5:24)},tx,cancellationToken:ct));
    }
    private Task AuditAsync(MySqlConnection connection,MySqlTransaction tx,string id,string action,CancellationToken ct)=>connection.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,occurred_at) VALUES(@id,@action,'member_user',@now)",new{id,action,now=clock.UtcNow},tx,cancellationToken:ct));
    private static bool TokenFormat(string? token)=>token is {Length:64}&&token.All(char.IsAsciiHexDigit);
    private static MemberSession Session(UserRow user,string token,DateTimeOffset expiry)=>new(user.id,user.full_name,user.email,user.preferred_culture,token,expiry);
    private sealed class UserRow
    {
        public string id {get;set;}=""; public string full_name {get;set;}="";public string email {get;set;}="";
        public string password_hash {get;set;}="";public string security_stamp {get;set;}="";public string preferred_culture {get;set;}="pt-BR";
        public bool is_blocked {get;set;}public bool email_confirmed {get;set;}public int failed_attempts {get;set;}public DateTimeOffset? lockout_until {get;set;}
    }
    private sealed class SessionRow
    {
        public string id {get;set;}="";public string full_name {get;set;}="";public string email {get;set;}="";public string preferred_culture {get;set;}="pt-BR";public DateTimeOffset expires_at {get;set;}
    }
    private sealed class SessionListRow
    {
        public string id_hash {get;set;}="";public DateTimeOffset created_at {get;set;}public DateTimeOffset expires_at {get;set;}
    }
}
