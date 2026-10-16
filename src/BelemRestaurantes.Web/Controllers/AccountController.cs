using System.Globalization;
using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BelemRestaurantes.Web.Controllers;

[Route("account")]
[RequestSizeLimit(16384)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Authorize(AuthenticationSchemes = MemberAuth.Scheme)]
[EnableRateLimiting("account")]
public sealed class AccountController(IMemberAccounts accounts, IStringLocalizer<SharedResource> text) : Controller
{
    private string Subject() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string SessionToken() => User.FindFirstValue("sid")!;
    private string Culture() => CultureInfo.CurrentUICulture.Name;

    [AllowAnonymous, HttpGet("register")]
    public IActionResult Register() => View(new RegisterInput());
    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("registration")]
    public async Task<IActionResult> RegisterSubmit(RegisterInput input, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(input.Website)) return Message("CheckEmail");
        if (!ModelState.IsValid || !input.AcceptTerms || AdminPasswordPolicy.Validate(input.Password).Count > 0)
        { ModelState.Clear(); ModelState.AddModelError("", text["RegistrationInvalid"]); input.Password = ""; input.ConfirmPassword = ""; return View("Register", input); }
        try { await accounts.RegisterAsync(new(input.FullName, input.Email, input.Password, input.AcceptTerms, Culture()), ct); }
        catch (ArgumentException)
        { ModelState.Clear(); ModelState.AddModelError("", text["RegistrationInvalid"]); input.Password = ""; input.ConfirmPassword = ""; return View("Register", input); }
        return Message("CheckEmail");
    }

    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => View(new MemberLoginInput { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> LoginSubmit(MemberLoginInput input, CancellationToken ct)
    {
        var session = ModelState.IsValid ? await accounts.LoginAsync(input.Email, input.Password, input.Remember, ct) : null;
        if (session is null)
        { ModelState.Clear(); ModelState.AddModelError("", text["MemberLoginFailed"]); input.Password = ""; return View("Login", input); }
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, session.UserId), new Claim(ClaimTypes.Name, session.Name), new Claim("sid", session.SessionToken), new Claim("email_verified", "true") }, MemberAuth.Scheme);
        await HttpContext.SignInAsync(MemberAuth.Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = input.Remember, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
        return LocalRedirect(Url.IsLocalUrl(input.ReturnUrl) ? input.ReturnUrl! : "/account");
    }

    [AllowAnonymous, HttpGet("forgot")]
    public IActionResult Forgot() => View("Email", new EmailInput());
    [AllowAnonymous, HttpPost("forgot")]
    public async Task<IActionResult> ForgotSubmit(EmailInput input, CancellationToken ct)
    { if (ModelState.IsValid) await accounts.RequestTokenAsync(input.Email, "reset", Culture(), ct); return Message("RecoveryNeutral"); }
    [AllowAnonymous, HttpGet("resend")]
    public IActionResult Resend() { ViewData["ConfirmEmail"] = true; return View("Email", new EmailInput()); }
    [AllowAnonymous, HttpPost("resend")]
    public async Task<IActionResult> ResendSubmit(EmailInput input, CancellationToken ct)
    { if (ModelState.IsValid) await accounts.RequestTokenAsync(input.Email, "confirm", Culture(), ct); return Message("CheckEmail"); }
    [AllowAnonymous, HttpGet("confirm")]
    public IActionResult Confirm() { Response.Headers["Cache-Control"] = "no-store"; return View(); }
    [AllowAnonymous, HttpPost("confirm")]
    public async Task<IActionResult> ConfirmSubmit(string token, CancellationToken ct) => Message(await accounts.ConfirmEmailAsync(token, ct) ? "EmailConfirmed" : "InvalidLink");
    [AllowAnonymous, HttpGet("reset")]
    public IActionResult Reset() { Response.Headers["Cache-Control"] = "no-store"; return View(new ResetInput()); }
    [AllowAnonymous, HttpPost("reset")]
    public async Task<IActionResult> ResetSubmit(ResetInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || AdminPasswordPolicy.Validate(input.Password).Count > 0)
        { ModelState.Clear(); ModelState.AddModelError("", text["RegistrationInvalid"]); input.Password = ""; input.ConfirmPassword = ""; return View("Reset", input); }
        return Message(await accounts.ResetPasswordAsync(input.Token, input.Password, ct) ? "PasswordReset" : "InvalidLink");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    { Response.Headers["Cache-Control"] = "no-store"; return View(await accounts.SessionsAsync(Subject(), SessionToken(), ct)); }
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    { await accounts.RevokeSessionAsync(Subject(), SessionToken(), ct); await HttpContext.SignOutAsync(MemberAuth.Scheme); return Redirect("/"); }
    [HttpPost("sessions/revoke-others")]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct)
    { await accounts.RevokeOtherSessionsAsync(Subject(), SessionToken(), ct); return Redirect("/account"); }
    [AllowAnonymous, HttpGet("terms")]
    public IActionResult Terms() => View("Policy");
    [AllowAnonymous, HttpGet("privacy")]
    public IActionResult Privacy() { ViewData["Privacy"] = true; return View("Policy"); }
    private IActionResult Message(string key) { ViewData["Message"] = text[key].Value; return View("Message"); }
}
