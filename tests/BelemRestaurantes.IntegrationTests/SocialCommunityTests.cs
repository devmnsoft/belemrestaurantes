using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

public sealed class SocialCommunityTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = "Server=localhost;Database=belrestaurante;User ID=social_test;Password=unused;SslMode=Required",
                ["Discovery:Enabled"] = "false"
            })));

    private HttpClient Client(bool autoRedirect = false) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = autoRedirect
    });

    [Theory]
    [InlineData("/experience/create")]
    [InlineData("/account")]
    [InlineData("/account/export-data")]
    [InlineData("/admin/community/experiences")]
    [InlineData("/admin/community/photos")]
    [InlineData("/admin/community/reports")]
    [InlineData("/admin/community/members")]
    [InlineData("/admin/community/lgpd")]
    [InlineData("/admin/community/cuia")]
    [InlineData("/admin/community/smtp")]
    public async Task Protected_routes_redirect_to_login(string path)
    {
        using var client = Client(autoRedirect: false);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login", response.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Media_photo_path_traversal_is_blocked()
    {
        using var client = Client(autoRedirect: false);
        using var response = await client.GetAsync("/media/photo/..%2f..%2fappsettings.json");
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
    }

    [Fact]
    public async Task Media_thumb_path_traversal_is_blocked()
    {
        using var client = Client(autoRedirect: false);
        using var response = await client.GetAsync("/media/thumb/..%2f..%2fappsettings.json");
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        using var client = Client(autoRedirect: false);
        using var response = await client.PostAsync("/account/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPassword"] = "oldPass123!",
            ["NewPassword"] = "newPass123!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login", response.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => factory.Dispose();
}
