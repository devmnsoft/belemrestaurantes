using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

public sealed class PartnerEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = "Server=localhost;Database=belrestaurante;User ID=partner_test;Password=unused;SslMode=Required",
                ["Discovery:Enabled"] = "false"
            })));

    private HttpClient Client(bool autoRedirect = false) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = autoRedirect
    });

    [Theory]
    [InlineData("/partner/dashboard")]
    [InlineData("/partner/1")]
    [InlineData("/partner/1/schedule")]
    [InlineData("/partner/1/announcements")]
    [InlineData("/partner/1/stats")]
    [InlineData("/partner/1/plans")]
    [InlineData("/partner/1/menu")]
    [InlineData("/admin/partner/claims")]
    public async Task Protected_partner_routes_redirect_unauthenticated_to_login(string path)
    {
        using var client = Client(autoRedirect: false);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login", response.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_trails_endpoint_is_accessible()
    {
        using var client = Client(autoRedirect: true);
        using var response = await client.GetAsync("/roteiros");
        // Rota pública de turismo
        // The deliberately unavailable SQL fixture returns 503, never an authentication redirect or an unhandled 500.
        Assert.Contains(response.StatusCode,new[]{HttpStatusCode.OK,HttpStatusCode.ServiceUnavailable});
    }

    public void Dispose() => factory.Dispose();
}
