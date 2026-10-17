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

        TempData["PendingCredentialVersion"] = CredentialVersion(user);

        if (!user.TwoFactorEnabled)
        {
            var secret = totp.GenerateSecret();
            TempData["PendingUntil"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString();
            TempData["PendingEnrollmentSecret"] = secret;
            TempData["PendingEnrollmentUser"] = user.Id;
            return RedirectToAction("Enrol");
        }

        TempData["PendingUntil"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString();
        TempData["PendingUserId"] = user.Id;
        return RedirectToAction("VerifyMfa", new { returnUrl });
    }

    [AllowAnonymous]
    [HttpGet("mfa/verify")]
    public IActionResult VerifyMfa(string? returnUrl = null)
    {
        if (TempData.Peek("PendingUserId") is not string)
        {
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
        if (TempData["PendingUserId"] is not string userId)
        {
            return RedirectToAction("Login");
        }

        if (!PendingValid()) return RedirectToAction("Login");
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.IsLockedOut(DateTimeOffset.UtcNow) || TempData.Peek("PendingCredentialVersion") as string != CredentialVersion(user) || !user.TwoFactorEnabled || user.TwoFactorSecret is null or { Length: 0 })
        {
            return RedirectToAction("Login");
        }

        string secret;
        try { secret = System.Text.Encoding.UTF8.GetString(protection.CreateProtector("Admin.Totp.v1").Unprotect(user.TwoFactorSecret)); }
        catch (System.Security.Cryptography.CryptographicException) { return RedirectToAction("Login"); }
        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            ModelState.AddModelError(string.Empty, text["InvalidCode"].Value);
            return View("VerifyMfa");
        }

        if (!await ConsumeCodeAsync(user.Id, code, secret, ct)) return RedirectToAction("Login");
        await users.ResetFailuresAsync(user.Id, DateTimeOffset.UtcNow, ct);
        await IssueCookieAsync(user, ct);
        return RedirectSafe(returnUrl);
    }

    [AllowAnonymous]
    [HttpGet("mfa/enrol")]
    public IActionResult Enrol()
    {
        if (TempData["PendingEnrollmentSecret"] is not string secret)
        {
            return RedirectToAction("Login");
        }

        TempData["PendingEnrollmentSecret"] = secret;
        ViewData["Secret"] = secret;
        return View("Enrol");
    }

    [AllowAnonymous]
    [HttpPost("mfa/enrol")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrolSubmit(string code, CancellationToken ct)
    {
        if (TempData["PendingEnrollmentSecret"] is not string secret || TempData["PendingEnrollmentUser"] is not string userId)
        {
            return RedirectToAction("Login");
        }

        if (!PendingValid()) return RedirectToAction("Login");
        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(userId, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            TempData["PendingEnrollmentSecret"] = secret;
            TempData["PendingEnrollmentUser"] = userId;
            ViewData["Secret"] = secret;
            ModelState.AddModelError(string.Empty, text["InvalidCode"].Value);
            return View("Enrol");
        }

        var pendingUser = await users.FindByIdAsync(userId, ct);
        if (pendingUser is null || pendingUser.IsLockedOut(DateTimeOffset.UtcNow) || TempData.Peek("PendingCredentialVersion") as string != CredentialVersion(pendingUser) || pendingUser.TwoFactorEnabled)
            return RedirectToAction("Login");
        if (!await users.EnableTwoFactorAsync(userId, protection.CreateProtector("Admin.Totp.v1").Protect(System.Text.Encoding.UTF8.GetBytes(secret)), ct) ||
            !await ConsumeCodeAsync(userId, code, secret, ct)) return RedirectToAction("Login");
        var user = await users.FindByIdAsync(userId, ct);
        if (user is not null)
        {
            await IssueCookieAsync(user, ct);
        }

        return RedirectToAction("Dashboard");
    }

    [AllowAnonymous]
    [HttpGet("access-denied")]
    public IActionResult AccessDenied() => Problem(statusCode: 403, title: text["AccessDenied"].Value);

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
            new("credential_version", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash))))
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, AdminAuthDefaults.Scheme);
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

    private bool PendingValid() => long.TryParse(TempData.Peek("PendingUntil") as string, out var until) && until > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private IActionResult RedirectSafe(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard");
    }
}