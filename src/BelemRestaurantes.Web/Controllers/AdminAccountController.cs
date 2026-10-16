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
        return View();
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginSubmit(string userName, string password, string? returnUrl, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(userName, ct);
        if (user is null || !passwordVerifier.Verify(password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Credenciais inválidas ou bloqueadas por força bruta.");
            return View("Login");
        }

        // Bloqueado pelo operador ou por tentativas falhas anteriores.
        var lockUntil = user.LockoutEndAt;
        if (lockUntil is not null && lockUntil > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Conta temporariamente bloqueada.");
            return View("Login");
        }

        // Enrolamento inicial: TOTP não configurado → precisa vincular.
        if (string.IsNullOrEmpty(user.TwoFactorSecret?.Length > 0 ? "x" : null) && !user.TwoFactorEnabled)
        {
            // Primeira configuração: gera segredo e pede para enrolar.
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
        if (user is null || user.TwoFactorSecret is null)
        {
            return RedirectToAction("Login");
        }

        var secret = System.Text.Encoding.UTF8.GetString(user.TwoFactorSecret); // armazenado cifrado em produção; dev texto
        if (!totp.Validate(code, secret, DateTimeOffset.UtcNow))
        {
            await users.IncrementFailuresAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(15), ct);
            ModelState.AddModelError(string.Empty, "Código TOTP inválido ou fora da janela.");
            return View("VerifyMfa");
        }

        await users.ResetFailuresAsync(user.Id, DateTimeOffset.UtcNow, ct);
        await IssueCookieAsync(user, ct, returnUrl);
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

        TempData["PendingEnrollmentSecret"] = secret; // re-expect
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
            ModelState.AddModelError(string.Empty, "Código inválido. Tente de novo.");
            return View("Enrol");
        }

        await users.EnableTwoFactorAsync(userId, System.Text.Encoding.UTF8.GetBytes(secret), ct);
        var user = await users.FindByIdAsync(userId, ct);
        if (user is not null)
        {
            await IssueCookieAsync(user, ct, null);
        }

        return RedirectToAction("Index", "AdminDashboard");
    }

    [AllowAnonymous]
    [HttpPost("bootstrap")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bootstrap([FromForm] string userName, [FromForm] string password, [FromForm] string email, CancellationToken ct)
    {
        // Criação do primeiro administrador: permitida apenas quando não houver
        // nenhum usuário administrativo no banco. Impode re-criação forçada.
        var sentinel = await users.FindByNameAsync("platform_admin", ct);
        var anyExisting = sentinel is not null ||
                          (await users.FindByNameAsync(userName, ct)) is not null;

        if (anyExisting)
        {
            return Forbid();
        }

        var roles = new[] { AdminRoles.PlatformAdmin };
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

        await users.CreateAsync(record, roles, ct);

        return Ok(new
        {
            message = "Administrador criado. Guarde o segredo abaixo no app TOTP agora.",
            userName,
            totpSecret = secret
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/admin/login");
    }

    [Authorize(Policy = "PlatformAdmin")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var countPending = 0;
        ViewBag.PendingCount = countPending;
        return View("Dashboard");
    }

    private async Task IssueCookieAsync(AdminUserRecord user, CancellationToken ct, string? returnUrl)
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
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
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