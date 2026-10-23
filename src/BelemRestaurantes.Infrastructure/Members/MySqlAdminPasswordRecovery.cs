using System.Security.Cryptography;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
namespace BelemRestaurantes.Infrastructure.Members;
public sealed class MySqlAdminPasswordRecovery(MySqlConnectionFactory factory,IAdminUserRepository users,IPasswordVerifier passwords,MailPayloadProtector protector,IConfiguration config):IAdminPasswordRecovery
{
    private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public async Task RequestAsync(string identifier,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(identifier)||identifier.Length>200)return;
        var user=await users.FindByNameAsync(identifier.Trim(),ct);if(user is null||string.IsNullOrWhiteSpace(user.Email))return;
        var origin=config["MemberAccounts:PublicOrigin"]??"https://belemrestaurantes.com.br";
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new InvalidOperationException("Origem de recuperação inválida.");
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var enabled=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM admin_users WHERE id=@id AND disabled=0 AND is_disabled=0 FOR UPDATE",new{id=user.Id},tx,cancellationToken:ct));if(enabled!=1)return;
        var recent=await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM admin_password_tokens WHERE user_id=@id AND created_at>DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE)",new{id=user.Id},tx,cancellationToken:ct));if(recent>0)return;
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync(new CommandDefinition("UPDATE admin_password_tokens SET used_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND used_at IS NULL",new{id=user.Id},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO admin_password_tokens(token_hash,user_id,credential_version,created_at,expires_at) VALUES(@hash,@id,@version,UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 30 MINUTE))",new{hash=Hash(token),id=user.Id,version=Hash(user.PasswordHash)},tx,cancellationToken:ct));
        var message=new TransactionalMail(user.Email,user.UserName,"reset",uri.GetLeftPart(UriPartial.Authority)+"/admin/reset#token="+token,"pt-BR");
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO transactional_email_outbox(id,payload,next_attempt_at,created_at,expires_at) VALUES(@id,@payload,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 30 MINUTE))",new{id=Guid.NewGuid().ToString("N"),payload=protector.Protect(message)},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES('system:admin-recovery','admin.password_reset_requested','admin_user',@metadata,UTC_TIMESTAMP(6))",new{metadata=System.Text.Json.JsonSerializer.Serialize(new{userId=user.Id})},tx,cancellationToken:ct));await tx.CommitAsync(ct);
    }
    public async Task<bool> ResetAsync(string token,string password,CancellationToken ct)
    {
        if(token is not {Length:64}||!token.All(char.IsAsciiHexDigit)||AdminPasswordPolicy.Validate(password).Count>0)return false;
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var row=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("SELECT u.id Id,u.password_hash PasswordHash,t.credential_version Version FROM admin_password_tokens t JOIN admin_users u ON u.id=t.user_id WHERE t.token_hash=@hash AND t.used_at IS NULL AND t.expires_at>UTC_TIMESTAMP(6) AND u.disabled=0 AND u.is_disabled=0 FOR UPDATE",new{hash=Hash(token)},tx,cancellationToken:ct));
        if(row is null||row.Version!=Hash(row.PasswordHash)||passwords.Verify(password,row.PasswordHash))return false;
        await db.ExecuteAsync(new CommandDefinition("UPDATE admin_users SET password_hash=@password,access_failed_count=0,lockout_end_at=NULL,must_change_password=0 WHERE id=@id",new{id=row.Id,password=passwords.Hash(password)},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("UPDATE admin_password_tokens SET used_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND used_at IS NULL",new{id=row.Id},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES('system:admin-recovery','admin.password_reset','admin_user',@metadata,UTC_TIMESTAMP(6))",new{metadata=System.Text.Json.JsonSerializer.Serialize(new{userId=row.Id})},tx,cancellationToken:ct));await tx.CommitAsync(ct);return true;
    }
    private sealed class Row{public string Id{get;set;}="";public string PasswordHash{get;set;}="";public string Version{get;set;}="";}
}
