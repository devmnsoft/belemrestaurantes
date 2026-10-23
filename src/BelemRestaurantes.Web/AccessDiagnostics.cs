using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
namespace BelemRestaurantes.Web;
internal static class AccessDiagnostics
{
    internal static async Task RunAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();var sp=scope.ServiceProvider;await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var admins=await db.QuerySingleAsync("SELECT COUNT(*) accounts,SUM(two_factor_enabled) mfa_enabled,SUM(lockout_end_at>UTC_TIMESTAMP()) locked,SUM(disabled OR is_disabled) disabled FROM admin_users");
        var members=await db.QuerySingleAsync("SELECT COUNT(*) accounts,SUM(email_confirmed) confirmed,SUM(is_blocked) blocked,SUM(lockout_until>UTC_TIMESTAMP()) locked FROM member_users");
        var events=await db.QueryAsync("SELECT action,JSON_UNQUOTE(JSON_EXTRACT(metadata,'$.reason')) reason,COUNT(*) total FROM audit_events WHERE occurred_at>DATE_SUB(UTC_TIMESTAMP(),INTERVAL 2 DAY) AND action IN ('mfa_verify_rejected','mfa_verify_failed','mfa_enrol_rejected','login_failed') GROUP BY action,reason");
        var secrets=await db.QueryAsync<byte[]>("SELECT two_factor_secret FROM admin_users WHERE two_factor_enabled=1 AND two_factor_secret IS NOT NULL");var readable=0;var protector=sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.Totp.v1");
        foreach(var secret in secrets)try{var bytes=protector.Unprotect(secret);System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);readable++;}catch(System.Security.Cryptography.CryptographicException){}
        var config=sp.GetRequiredService<IConfiguration>();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{admins,members,events,mfaSecretsReadable=readable,mfaSecretsTotal=secrets.Count(),smtpEnabled=config.GetValue<bool>("Smtp:Enabled"),smtpConfigured=!string.IsNullOrWhiteSpace(config["Smtp:Host"])&&!string.IsNullOrWhiteSpace(config["Smtp:Username"])&&!string.IsNullOrWhiteSpace(config["Smtp:Password"])}));
    }
}
