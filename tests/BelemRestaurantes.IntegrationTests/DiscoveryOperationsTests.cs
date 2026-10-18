using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BelemRestaurantes.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class DiscoveryOperationsTests
{
    [Theory]
    [InlineData("/admin/discovery", "Descoberta por cidade e fonte")]
    [InlineData("/admin/crawler", "Cuia Crawler")]
    [InlineData("/admin/discovery360", "Discovery")]
    [InlineData("/account", "Perfil")]
    [InlineData("/account/favorites", "favoritos")]
    [InlineData("/partner/dashboard", "Parceiro")]
    public async Task Operator_views_render_real_database_metrics(string route, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development")
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString() }))
                .ConfigureServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "test-operator";
                        options.DefaultChallengeScheme = "test-operator";
                    }).AddScheme<AuthenticationSchemeOptions, OperatorHandler>("test-operator", _ => { });
                    services.PostConfigure<AuthenticationOptions>(options =>
                        options.Schemes.First(s => s.Name == BelemRestaurantes.Application.Abstractions.MemberAuth.Scheme).HandlerType = typeof(OperatorHandler));
                }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, html);
        Assert.Contains(expected, WebUtility.HtmlDecode(html));
        // Snapshot somente do HTML autorizado por este host de teste. Nenhuma
        // credencial real ou exceção de autenticação é adicionada à aplicação.
        var output = Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, route.Replace('/', '-').TrimStart('-') + ".html"), html);
        }
    }

    private sealed class OperatorHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "test-operator"), new Claim(ClaimTypes.Name, "Operador de teste"),
                new Claim(ClaimTypes.Role, AdminRoles.PlatformAdmin), new Claim("amr", "mfa")
                , new Claim("sid", "fixture-session"), new Claim("email_verified", "true")
            }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
