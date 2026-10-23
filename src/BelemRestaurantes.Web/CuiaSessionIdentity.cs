using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace BelemRestaurantes.Web;

public static class CuiaSessionIdentity
{
    public static string GetOrCreate(HttpContext context)
    {
        const string cookie = "cuia-usage-session";
        if(context.Items[cookie] is string existing)return existing;
        var protector = context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("Cuia.UsageSession.v1");
        if (context.Request.Cookies[cookie] is {} value)
            try
            {
                var parts = protector.Unprotect(value).Split('|');
                if (parts.Length == 2 && parts[0].Length == 32 && parts[0].All(Uri.IsHexDigit) && long.TryParse(parts[1], out var issued) &&
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issued is >= 0 and < 86400){context.Items[cookie]=parts[0];return parts[0];}
            }
            catch (CryptographicException) { /* Invalid or expired cookies receive a fresh opaque identifier. */ }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        context.Response.Cookies.Append(cookie, protector.Protect(id + "|" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(24), Path = "/", IsEssential = true });
        context.Items[cookie]=id;
        return id;
    }
}
