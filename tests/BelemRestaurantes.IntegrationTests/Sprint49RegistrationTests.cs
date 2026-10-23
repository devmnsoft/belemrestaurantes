using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint49RegistrationTests
{
    [Theory]
    [InlineData("Cpf", "00000000000", "CPF inválido")]
    [InlineData("ConfirmPassword", "Different123!", "confirmação deve ser igual")]
    [InlineData("AcceptTerms", "false", "Aceite os termos")]
    [InlineData("Password", "weak", "maiúscula")]
    [InlineData("Cnpj", "123", "CNPJ inválido")]
    public async Task InvalidPostShowsSpecificErrorsAndNeverEchoesPassword(string field, string value, string expected)
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString(), ["Database:ApplyMigrations"]="false", ["MailEncryption:KeyRingPath"]=DatabaseEnvironment.MailKeys
        })));
        using var client = app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var page = await client.GetStringAsync("/account/register");
        var token = Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var data = new Dictionary<string,string>{["FullName"]="Synthetic Test",["Email"]=Guid.NewGuid()+"@example.invalid",["Cpf"]=Sprint48IdentityTests.NewCpf(),["Password"]="ReliableTest123!",["ConfirmPassword"]="ReliableTest123!",["AcceptTerms"]="true",["__RequestVerificationToken"]=WebUtility.HtmlDecode(token)};
        data[field]=value;
        using var response = await client.PostAsync("/account/register",new FormUrlEncodedContent(data));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains(expected,html); Assert.Contains("validation-summary-errors",html);
        Assert.DoesNotContain("value=\"ReliableTest123!\"",html); Assert.DoesNotContain("value=\"Different123!\"",html);
    }

    [Theory]
    [InlineData("/favicon.ico",200)]
    [InlineData("/favicon.svg",200)]
    [InlineData("/images/does-not-exist.png",404)]
    [InlineData("/js/does-not-exist.js",404)]
    public async Task PublicAssetsNeverRedirectToAdmin(string path,int status)
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Database:ApplyMigrations"]="false"})));
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        using var response=await client.GetAsync(path);Assert.Equal(status,(int)response.StatusCode);Assert.Null(response.Headers.Location);
    }
}
