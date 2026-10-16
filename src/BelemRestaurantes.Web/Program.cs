using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Application;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    // Revalida conta (ativa/bloqueada/papéis) a cada acesso por cookie admin.
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services
    .AddAuthentication(AdminAuthDefaults.Scheme)
    .AddCookie(AdminAuthDefaults.Scheme, options =>
    {
        options.Cookie.Name = ".BelRest.AdminAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/access-denied";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RestaurantModerator",
        p => p.RequireAuthenticatedUser().RequireRole(AdminRoles.Moderator, AdminRoles.PlatformAdmin));
    options.AddPolicy("PlatformOperator",
        p => p.RequireAuthenticatedUser().RequireRole(AdminRoles.Operator, AdminRoles.PlatformAdmin));
    options.AddPolicy("PlatformAdmin",
        p => p.RequireAuthenticatedUser().RequireRole(AdminRoles.PlatformAdmin));
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
    options.AddFixedWindowLimiter("public", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("admin", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.AutoReplenishment = true;
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; script-src 'self'; form-action 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; upgrade-insecure-requests";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = static async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
        }));
    }
});
app.MapControllers().RequireRateLimiting("public");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}").RequireRateLimiting("public");

app.Run();