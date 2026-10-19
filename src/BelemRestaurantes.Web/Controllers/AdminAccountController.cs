using Microsoft.AspNetCore.DataProtection;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BelemRestaurantes.Web.Controllers;

[Route("admin")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("admin")]
public sealed class AdminAccountController(
    IAdminUserRepository users,
    IPasswordVerifier passwordVerifier,
    ITotpProvider totp,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    IAuditWriter audit,
    Microsoft.Extensions.Localization.IStringLocalizer<SharedResource> text) : Controller
{
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View("Login");
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginSubmit(string userName, string password, string? returnUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 100 || string.IsNullOrEmpty(password) || password.Length > 1024)
        {
            ModelState.AddModelError(string.Empty, text["InvalidCredentials"].Value);
            return View("Login");
        }
        var user = await users.FindByNameAsync(userName, ct);
        var validPassword = passwordVerifier.Verify(password, user?.PasswordHash ?? DummyHash);
        if (user is null || user.IsLockedOut(DateTimeOffset.UtcNow) || !validPassword)
        {
            if (user is not null)
                await users.IncrementFailuresAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            await audit.WriteAsync("login_failed", "admin_user", null, null, ct);
            ModelState.AddModelError(string.Empty, text["InvalidCredentials"].Value);
            return View("Login");
        }

        if (!user.TwoFactorEnabled)
        {
            var secret = totp.GenerateSecret();
            SetChallenge(user,secret);
            return RedirectToAction("Enrol");
        }

        SetChallenge(user,null);
        return RedirectToAction("VerifyMfa", new { returnUrl });
    }

    [AllowAnonymous]
    [HttpGet("mfa/verify")]
    public IActionResult VerifyMfa(string? returnUrl = null)
    {
        if (ReadChallenge(false) is null)
        {
            TempData["Error"] = "Sessão de verificação expirada ou inválida. Por favor, entre com suas credenciais.";
            return RedirectToAction("Login");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View("VerifyMfa");
    }

    [AllowAnonymous]
    [HttpPost("mfa/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyMfaSubmit(string code, string? returnUrl, CancellationToken ct)
    {
        var challenge = ReadChallenge(false);
        if (challenge is null)
        {
            await audit.WriteAsync("mfa_verify_rejected", "admin_user", null, "{\"reason\":\"challenge_missing\"}", ct);
            TempData["Error"] = "A sessão de verificação expirou. Entre novamente.";
            return RedirectToAction("Login");
        }

        var userId = challenge.UserId;
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.IsLockedOut(DateTimeOffset.UtcNow) || challenge.Version != CredentialVersion(user) || !user.TwoFactorEnabled || user.TwoFactorSecret is null or { Length: 0 })
        {
            await audit.WriteAsync("mfa_verify_rejected", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "user_invalid_or_locked" }), ct);
            TempData["Error"] = "Conta indisponível, bloqueada ou credencial alterada. Entre novamente.";
            return RedirectToAction("Login");
        }

        var secret = await UnprotectTotpSecretAsync(user, ct);
        if (secret is null)
        {
            await audit.WriteAsync("mfa_verify_rejected", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "secret_unprotect_failed" }), ct);
            TempData["Error"] = "Falha ao verificar segundo fator de segurança. Contate o suporte técnico.";
            return RedirectToAction("Login");
        }

        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            await audit.WriteAsync("mfa_verify_failed", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "invalid_code" }), ct);
            ModelState.AddModelError(string.Empty, text["InvalidCode"].Value);
            return View("VerifyMfa");
        }

        if (!await ConsumeCodeAsync(user.Id, code, secret, ct))
        {
            await audit.WriteAsync("mfa_verify_rejected", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "code_replay" }), ct);
            ModelState.AddModelError(string.Empty, "Este código já foi utilizado. Aguarde a troca do código (30 segundos).");
            return View("VerifyMfa");
        }

        ClearChallenge();
        await users.ResetFailuresAsync(user.Id, DateTimeOffset.UtcNow, ct);
        await IssueCookieAsync(user, ct);
        if (user.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        return RedirectSafe(returnUrl);
    }

    [AllowAnonymous]
    [HttpGet("mfa/enrol")]
    public IActionResult Enrol()
    {
        var challenge = ReadChallenge(true);
        if (challenge?.Secret is not string secret)
        {
            TempData["Error"] = "Sessão de configuração de segundo fator expirada. Entre novamente.";
            return RedirectToAction("Login");
        }

        ViewData["Secret"] = secret;
        return View("Enrol");
    }

    [AllowAnonymous]
    [HttpPost("mfa/enrol")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrolSubmit(string code, CancellationToken ct)
    {
        var challenge = ReadChallenge(true);
        if (challenge?.Secret is not string secret)
        {
            await audit.WriteAsync("mfa_enrol_rejected", "admin_user", null, "{\"reason\":\"missing_context\"}", ct);
            TempData["Error"] = "A sessão de configuração expirou. Entre novamente.";
            return RedirectToAction("Login");
        }

        var userId = challenge.UserId;
        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(userId, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            ViewData["Secret"] = secret;
            ModelState.AddModelError(string.Empty, text["InvalidCode"].Value);
            return View("Enrol");
        }

        var pendingUser = await users.FindByIdAsync(userId, ct);
        if (pendingUser is null || pendingUser.IsLockedOut(DateTimeOffset.UtcNow) || challenge.Version != CredentialVersion(pendingUser) || pendingUser.TwoFactorEnabled)
        {
            await audit.WriteAsync("mfa_enrol_rejected", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "user_state" }), ct);
            TempData["Error"] = "Estado do usuário inválido para ativação de MFA.";
            return RedirectToAction("Login");
        }

        if (!await users.EnableTwoFactorAsync(userId, protection.CreateProtector("Admin.Totp.v1").Protect(System.Text.Encoding.UTF8.GetBytes(secret)), ct) ||
            !await ConsumeCodeAsync(userId, code, secret, ct))
        {
            await audit.WriteAsync("mfa_enrol_rejected", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId, reason = "enable_or_replay" }), ct);
            TempData["Error"] = "Não foi possível ativar o MFA. Tente novamente.";
            return RedirectToAction("Login");
        }

        var user = await users.FindByIdAsync(userId, ct);
        ClearChallenge();
        await users.ResetFailuresAsync(userId, DateTimeOffset.UtcNow, ct);
        if (user is not null)
        {
            await IssueCookieAsync(user, ct);
            if (user.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        }

        return RedirectSafe(null);
    }

    [AllowAnonymous]
    [HttpGet("access-denied")]
    public IActionResult AccessDenied() => Problem(statusCode: 403, title: text["AccessDenied"].Value);

    [Authorize,HttpGet("change-password")]
    public IActionResult ChangePassword()=>View();

    [Authorize,HttpPost("change-password"),ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePasswordSubmit(string currentPassword,string password,string confirmation,
        [FromServices] BelemRestaurantes.Infrastructure.MySql.MySqlConnectionFactory factory,CancellationToken ct)
    {
        var user=await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct);
        if(user is null)return Challenge();
        var errors=AdminPasswordPolicy.Validate(password??"");
        if(!passwordVerifier.Verify(currentPassword??"",user.PasswordHash)||errors.Count>0||password!=confirmation||passwordVerifier.Verify(password??"",user.PasswordHash))
        {ModelState.AddModelError("","Confira a senha atual, use uma nova senha forte e repita a confirmação.");return View("ChangePassword");}
        await using var db=await factory.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        var changed=await Dapper.SqlMapper.ExecuteAsync(db,new Dapper.CommandDefinition("UPDATE admin_users SET password_hash=@hash,must_change_password=FALSE,password_changed_at=UTC_TIMESTAMP(6) WHERE id=@id AND password_hash=@previous",new{id=user.Id,hash=passwordVerifier.Hash(password!),previous=user.PasswordHash},tx,cancellationToken:ct));
        if(changed!=1)return Conflict();
        await Dapper.SqlMapper.ExecuteAsync(db,new Dapper.CommandDefinition("INSERT INTO audit_events(actor_subject,action,entity_type,occurred_at) VALUES(@id,'admin.password_changed','admin_user',UTC_TIMESTAMP(6))",new{id=user.Id},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        await HttpContext.SignOutAsync(AdminAuthDefaults.Scheme);
        TempData["Flash"]="Senha alterada. Entre novamente com MFA.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await audit.WriteAsync("logout", "admin_user", null, null, ct);
        await HttpContext.SignOutAsync(AdminAuthDefaults.Scheme);
        return Redirect("/admin/login");
    }

    [Authorize(Policy = "AdminArea")]
    [HttpGet("dashboard")]
    public IActionResult Dashboard() => View("Dashboard");

    private async Task IssueCookieAsync(AdminUserRecord user, CancellationToken ct)
    {
        await audit.WriteAsync("login_success", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId = user.Id }), ct);
        var roles = await users.GetRolesAsync(user.Id, ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new("sub", user.Id),
            new("amr", "mfa"),
            new("must_change_password",user.MustChangePassword?"true":"false"),
            new("credential_version", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash))))
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, AdminAuthDefaults.Scheme);
        HttpContext.User=new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            AdminAuthDefaults.Scheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
    }

    private async Task<bool> ConsumeCodeAsync(string userId, string code, string secret, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        for (var offset = 1; offset >= -1; offset--)
        {
            var instant = now.AddSeconds(offset * 30);
            if (totp.GenerateFor(instant, secret) == code)
                return await users.ConsumeTotpAsync(userId, instant.ToUnixTimeSeconds() / 30, ct);
        }
        return false;
    }

    private static readonly string DummyHash = $"600000.{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))}.{Convert.ToBase64String(new byte[32])}";

    private static string CredentialVersion(AdminUserRecord user) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash)));

    private const string ChallengeCookie=".BelRest.AdminMfa";
    private sealed record MfaChallenge(string UserId,string Version,string? Secret,DateTimeOffset Until);
    private CookieOptions ChallengeOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/admin/mfa",
        IsEssential = true
    };

    private void SetChallenge(AdminUserRecord user, string? secret)
    {
        var value = new MfaChallenge(user.Id, CredentialVersion(user), secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var options = ChallengeOptions();
        options.MaxAge = TimeSpan.FromMinutes(5);
        Response.Cookies.Append(ChallengeCookie, protection.CreateProtector("Admin.MfaChallenge.v1").Protect(System.Text.Json.JsonSerializer.Serialize(value)), options);
    }

    private MfaChallenge? ReadChallenge(bool enrollment)
    {
        if (!Request.Cookies.TryGetValue(ChallengeCookie, out var protectedValue)) return null;
        try
        {
            var value = System.Text.Json.JsonSerializer.Deserialize<MfaChallenge>(protection.CreateProtector("Admin.MfaChallenge.v1").Unprotect(protectedValue));
            return value is not null && value.Until > DateTimeOffset.UtcNow && (value.Secret is not null) == enrollment ? value : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            var legacyDiscriminators = new[]
            {
                "C:\\MNSOFT\\belemrestaurantes\\src\\BelemRestaurantes.Web\\",
                "C:\\MNSOFT\\belemrestaurantes\\src\\BelemRestaurantes.Web",
                "BelemRestaurantes.Web"
            };
            foreach (var disc in legacyDiscriminators)
            {
                try
                {
                    var sc = new ServiceCollection();
                    sc.AddDataProtection().SetApplicationName(disc);
                    using var sp = sc.BuildServiceProvider();
                    var legacyProtector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.MfaChallenge.v1");
                    var value = System.Text.Json.JsonSerializer.Deserialize<MfaChallenge>(legacyProtector.Unprotect(protectedValue));
                    return value is not null && value.Until > DateTimeOffset.UtcNow && (value.Secret is not null) == enrollment ? value : null;
                }
                catch (System.Security.Cryptography.CryptographicException) { }
            }
            return null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private async Task<string?> UnprotectTotpSecretAsync(AdminUserRecord user, CancellationToken ct)
    {
        if (user.TwoFactorSecret is null or { Length: 0 }) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(protection.CreateProtector("Admin.Totp.v1").Unprotect(user.TwoFactorSecret));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            var legacyDiscriminators = new[]
            {
                "C:\\MNSOFT\\belemrestaurantes\\src\\BelemRestaurantes.Web\\",
                "C:\\MNSOFT\\belemrestaurantes\\src\\BelemRestaurantes.Web",
                "BelemRestaurantes.Web",
                "belrestaurante"
            };
            foreach (var disc in legacyDiscriminators)
            {
                try
                {
                    var sc = new ServiceCollection();
                    sc.AddDataProtection().SetApplicationName(disc);
                    using var sp = sc.BuildServiceProvider();
                    var legacyProtector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.Totp.v1");
                    var plainBytes = legacyProtector.Unprotect(user.TwoFactorSecret);
                    var plain = System.Text.Encoding.UTF8.GetString(plainBytes);

                    try
                    {
                        var reProtected = protection.CreateProtector("Admin.Totp.v1").Protect(plainBytes);
                        await users.UpdateTwoFactorSecretAsync(user.Id, reProtected, ct);
                    }
                    catch { }

                    return plain;
                }
                catch (System.Security.Cryptography.CryptographicException) { }
            }
            return null;
        }
    }

    private void ClearChallenge() => Response.Cookies.Delete(ChallengeCookie, ChallengeOptions());

    private IActionResult RedirectSafe(string? returnUrl)
    {
        if(User.IsInRole(AdminRoles.Editor)&&!User.IsInRole(AdminRoles.PlatformAdmin)&&!User.IsInRole(AdminRoles.Moderator))return Redirect("/admin/editorial");
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard");
    }
}
