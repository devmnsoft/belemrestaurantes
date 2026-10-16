using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BelemRestaurantes.Web.Controllers;

[Route("admin")]
public sealed class AdminAccountController(
    IAdminUserRepository users,
    IPasswordVerifier passwordVerifier,
    ITotpProvider totp) : Controller
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
        var user = await users.FindByNameAsync(userName, ct);
        if (user is null || !passwordVerifier.Verify(password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Credenciais inválidas.");
            return View("Login");
        }

        if (user.LockoutEndAt is { } until && until > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Conta temporariamente bloqueada.");
            return View("Login");
        }

        if (user.TwoFactorSecret is { Length: > 0 } && !user.TwoFactorEnabled)
        {
            var secret = totp.GenerateSecret();
            TempData["PendingEnrollmentSecret"] = secret;
            TempData["PendingEnrollmentUser"] = user.Id;
            return RedirectToAction("Enrol");
        }

        TempData["PendingUserId"] = user.Id;
        return RedirectToAction("VerifyMfa", new { returnUrl });
    }

    [AllowAnonymous]
    [HttpGet("mfa/verify")]
    public IActionResult VerifyMfa(string? returnUrl = null)
    {
        if (TempData["PendingUserId"] is not string)
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

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.TwoFactorSecret is null or { Length: 0 })
        {
            return RedirectToAction("Login");
        }

        var secret = System.Text.Encoding.UTF8.GetString(user.TwoFactorSecret);
        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            ModelState.AddModelError(string.Empty, "Código TOTP inválido.");
            return View("VerifyMfa");
        }

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

        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            TempData["PendingEnrollmentSecret"] = secret;
            TempData["PendingEnrollmentUser"] = userId;
            ModelState.AddModelError(string.Empty, "Código inválido.");
            return View("Enrol");
        }

        await users.EnableTwoFactorAsync(userId, System.Text.Encoding.UTF8.GetBytes(secret), ct);
        var user = await users.FindByIdAsync(userId, ct);
        if (user is not null)
        {
            await IssueCookieAsync(user, ct);
        }

        return RedirectToAction("Dashboard");
    }

    [AllowAnonymous]
    [HttpPost("bootstrap")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bootstrap([FromForm] string userName, [FromForm] string password, [FromForm] string email, CancellationToken ct)
    {
        var existing = await users.FindByNameAsync(userName, ct);
        if (existing is not null)
        {
            return Forbid();
        }

        var validation = AdminPasswordPolicy.Validate(password);
        if (validation.Count > 0)
        {
            return BadRequest(new { errors = validation });
        }

        var secret = totp.GenerateSecret();
        var record = new AdminUserRecord(
            Guid.NewGuid().ToString("N"),
            userName,
            email,
            passwordVerifier.Hash(password),
            false,
            System.Text.Encoding.UTF8.GetBytes(secret),
            0,
            null,
            null);

        await users.CreateAsync(record, new[] { AdminRoles.PlatformAdmin }, ct);

        return Ok(new
        {
            message = "Administrador criado. Guarde o segredo do app autenticador agora.",
            userName,
            totpSecret = secret
        });
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/admin/login");
    }

    [Authorize]
    [HttpGet("dashboard")]
    public IActionResult Dashboard() => View("Dashboard");

    private async Task IssueCookieAsync(AdminUserRecord user, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user.Id, ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new("sub", user.Id)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
    }

    private IActionResult RedirectSafe(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard");
    }
}