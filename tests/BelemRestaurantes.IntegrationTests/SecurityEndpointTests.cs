using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.IntegrationTests;

public sealed class SecurityEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = "Server=localhost;Database=belrestaurante;User ID=security_test;Password=unused;SslMode=Required",
                ["Discovery:Enabled"] = "false"
            })));

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    [Theory]
    [InlineData("/admin/dashboard")]
    [InlineData("/api/admin/review/pending")]
    [InlineData("/community/favorite/1")]
    public async Task Protected_routes_challenge_anonymous_clients(string path)
    {
        using var client = Client();
        using var response = await client.GetAsync(path);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task Public_bootstrap_is_removed()
    {
        using var client = Client();
        using var response = await client.PostAsync("/admin/bootstrap", new FormUrlEncodedContent(new Dictionary<string,string>()));
        Assert.DoesNotContain(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText?.Contains("bootstrap", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task Login_requires_antiforgery_token()
    {
        using var client = Client();
        using var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["userName"] = "example", ["password"] = "invalid" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mfa_cannot_be_entered_without_password_challenge()
    {
        using var client = Client();
        using var response = await client.GetAsync("/admin/mfa/verify");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/admin/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Liveness_does_not_require_database_and_sets_security_headers()
    {
        using var client = Client();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    public void Dispose() => factory.Dispose();
}
