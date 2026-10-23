using System.Net;
using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BelemRestaurantes.IntegrationTests;
public sealed class Sprint48AvailabilityTests
{
    [Fact]
    public async Task RequestBoundaryLogsUnhandledFailureWithoutCookieOrExceptionMessageAndRethrows()
    {
        var logger = new DiagnosticsLogger();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers.Cookie = "fixture=private-cookie-value";
        var middleware = new BelemRestaurantes.Web.Diagnostics.RequestDiagnosticsMiddleware(
            _ => throw new InvalidOperationException("private-exception-message"), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-ID"));
        var messages = string.Join("\n", logger.Messages);
        Assert.Contains("InvalidOperationException", messages);
        Assert.Contains("500", messages);
        Assert.DoesNotContain("private-cookie-value", messages);
        Assert.DoesNotContain("private-exception-message", messages);
    }

    private sealed class DiagnosticsLogger : Microsoft.Extensions.Logging.ILogger<BelemRestaurantes.Web.Diagnostics.RequestDiagnosticsMiddleware>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task SqlFailureWhileValidatingAdminCookieFailsClosedWithoutExpiringTheCookie()
    {
        using var app=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.UseEnvironment("Development").ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:MySql"]="Server=127.0.0.1;Port=1;Database=belrestaurante;Uid=fixture;Pwd=unused;SslMode=None;ConnectionTimeout=1;",["Database:ApplyMigrations"]="false"
        })));
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        var options=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AdminAuthDefaults.Scheme);
        var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"fixture"),new Claim(ClaimTypes.Name,"fixture"),new Claim(ClaimTypes.Role,AdminRoles.PlatformAdmin),new Claim("amr","mfa")},AdminAuthDefaults.Scheme));
        var ticket=new AuthenticationTicket(principal,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(5)},AdminAuthDefaults.Scheme);
        var cookie=options.TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie",options.Cookie.Name+"="+cookie);
        using var response=await client.GetAsync("/admin/dashboard");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie",out var values)&&values.Any(v=>v.StartsWith(options.Cookie.Name+"=")));
        var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain(cookie,html);Assert.DoesNotContain("StackTrace",html);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }
}
