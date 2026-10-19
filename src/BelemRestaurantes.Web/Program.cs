using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Application;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
var keysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MNSOFT", "BelemRestaurantes", "keys");
Directory.CreateDirectory(keysPath);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("BelemRestaurantes")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddControllersWithViews(options =>
{
    options.MaxModelBindingCollectionSize = 2000;
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    // Revalida conta (ativa/bloqueada/papéis) a cada acesso por cookie admin.
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.ValueCountLimit = 4096);
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services
    .AddAuthentication(AdminAuthDefaults.Scheme)
    .AddCookie(MemberAuth.Scheme, options =>
    {
        options.Cookie.Name = ".BelRest.Member";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var token = context.Principal?.FindFirst("sid")?.Value;
            var session = token is null ? null : await context.HttpContext.RequestServices.GetRequiredService<IMemberAccounts>()
                .ValidateSessionAsync(token, context.HttpContext.RequestAborted);
            if (session is null || session.UserId != context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value)
                context.RejectPrincipal();
        };
    })
    .AddCookie(AdminAuthDefaults.Scheme, options =>
    {
        options.Cookie.Name = ".BelRest.AdminAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnValidatePrincipal = async context =>
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<IAdminUserRepository>();
            var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = id is null ? null : await users.FindByIdAsync(id, context.HttpContext.RequestAborted);
            var version = user is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash)));
            var roles = user is null ? [] : await users.GetRolesAsync(user.Id, context.HttpContext.RequestAborted);
            var cookieRoles = context.Principal?.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value) ?? [];
            var rotate = user?.MustRotateMfa == true ? "true" : "false";
            if (user is null || user.IsLockedOut(DateTimeOffset.UtcNow) || !user.TwoFactorEnabled ||
                context.Principal?.FindFirst("credential_version")?.Value != version ||
                context.Principal?.FindFirst("must_rotate_mfa")?.Value != rotate ||
                !roles.Order().SequenceEqual(cookieRoles.Order()))
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Admin principal rejected for {UserId}: userFound={UserFound}, locked={Locked}, mfa={Mfa}, versionMatch={VersionMatch}, rolesMatch={RolesMatch}",
                    id, user != null, user?.IsLockedOut(DateTimeOffset.UtcNow), user?.TwoFactorEnabled,
                    context.Principal?.FindFirst("credential_version")?.Value == version,
                    roles.Order().SequenceEqual(cookieRoles.Order()));
                context.RejectPrincipal();
            }
        };
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/access-denied";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MemberAuth.Policy, p => p.AddAuthenticationSchemes(MemberAuth.Scheme).RequireAuthenticatedUser().RequireClaim("email_verified", "true"));
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("RestaurantModerator",
        p => p.RequireAuthenticatedUser().RequireClaim("amr", "mfa").RequireRole(AdminRoles.Moderator, AdminRoles.PlatformAdmin));
    options.AddPolicy("PlatformOperator",
        p => p.RequireAuthenticatedUser().RequireClaim("amr", "mfa").RequireRole(AdminRoles.Operator, AdminRoles.PlatformAdmin));
    options.AddPolicy("AdminArea", p => p.RequireAuthenticatedUser().RequireClaim("amr", "mfa").RequireRole(AdminRoles.Moderator, AdminRoles.Operator, AdminRoles.PlatformAdmin));
    options.AddPolicy("PlatformAdmin",
        p => p.RequireAuthenticatedUser().RequireClaim("amr", "mfa").RequireRole(AdminRoles.PlatformAdmin));
    options.AddPolicy("ContentEditor",p=>p.RequireAuthenticatedUser().RequireClaim("amr","mfa").RequireRole(AdminRoles.Editor,AdminRoles.Moderator,AdminRoles.PlatformAdmin));
});

// JWT provisório removido (Sprint 02): não havia emissor configurado e a
// autenticação administrativa agora é cookie + MFA auditado.

builder.Services.AddHealthChecks().AddCheck<MySqlHealthCheck>("mysql");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAdminAuth(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 60,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    }));
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("registration", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("suggestions", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("cuia", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("admin", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 20,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    }));
});

var app = builder.Build();
if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    try
    {
        using var migrationScope = app.Services.CreateScope();
        await migrationScope.ServiceProvider.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(CancellationToken.None);
    }
    catch (MySqlConnector.MySqlException ex) when (ex.Number is 0 or 1042 or 1043 or 1044 or 1045 or 2003 or 2026)
    {
        app.Logger.LogWarning("Migrações não aplicadas neste processo porque o banco não está acessível ({ErrorNumber}).", ex.Number);
    }
}
if (args.Contains("--migrate") || args.Contains("--provision-admin"))
{
    await BelemRestaurantes.Web.AdminMaintenance.RunAsync(app.Services, args.Contains("--migrate"));
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});
app.UseRequestLocalization(BelemRestaurantes.Web.Localization.SupportedLanguages.Options());
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(self)";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-src https://www.openstreetmap.org https://www.youtube-nocookie.com; form-action 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; upgrade-insecure-requests";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async(context,next)=>{
    var path = context.Request.Path;
    var adminArea = path.StartsWithSegments("/admin") || path.StartsWithSegments("/api/admin");
    var authenticated = context.User.Identity?.IsAuthenticated == true;
    if(authenticated && adminArea && context.User.FindFirst("must_change_password")?.Value=="true"
        && !path.StartsWithSegments("/admin/change-password") && !path.StartsWithSegments("/admin/logout"))
    {context.Response.Redirect("/admin/change-password");return;}
    if(authenticated && adminArea && context.User.FindFirst("must_rotate_mfa")?.Value=="true"
        && !path.StartsWithSegments("/admin/mfa/rotate")
        && !path.StartsWithSegments("/admin/change-password")
        && !path.StartsWithSegments("/admin/logout"))
    {context.Response.Redirect("/admin/mfa/rotate");return;}
    await next();
});
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var actor = BelemRestaurantes.Infrastructure.MySql.MySqlAuditWriter.CurrentActor;
    actor.Value = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    using var logScope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = context.TraceIdentifier });
    try { await next(); }
    finally { actor.Value = null; }
});

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = static async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        }));
    }
}).AllowAnonymous();
app.MapControllers().RequireRateLimiting("public");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}").RequireRateLimiting("public");

app.Run();
public partial class Program { }
