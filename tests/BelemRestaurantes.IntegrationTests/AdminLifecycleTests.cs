using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class AdminLifecycleTests
{
    [LocalHomologationTheory][InlineData("superadmin.mnsoft",AdminRoles.PlatformAdmin)][InlineData("operador.teste",AdminRoles.Operator)][InlineData("editor.teste",AdminRoles.Editor)]
    public async Task RequestedPersistentAccountsHaveValidProtectedCredentialsAndPasswordChallenge(string name,string role)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("DPAPI exige Windows.");
        using var app=Factory();using var scope=app.Services.CreateScope();
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MNSOFT","BelemRestaurantes","homologacao.credentials.dpapi");
        var bytes=ProtectedData.Unprotect(await File.ReadAllBytesAsync(path),null,DataProtectionScope.CurrentUser);
        try
        {
            var credentials=JsonSerializer.Deserialize<Dictionary<string,string>>(bytes)!;
            var users=scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();var user=await users.FindByNameAsync(name,default);
            Assert.NotNull(user);Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordVerifier>().Verify(credentials[name],user.PasswordHash));
            Assert.Contains(role,await users.GetRolesAsync(user.Id,default));Assert.True(user.MustChangePassword);
            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
            var token=Token(await client.GetStringAsync("/admin/login"));
            var response=await client.PostAsync("/admin/login",Form(token,("userName",name),("password",credentials[name])));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("/admin/mfa/",response.Headers.Location!.ToString());
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    [Theory][InlineData(AdminRoles.PlatformAdmin)][InlineData(AdminRoles.Operator)][InlineData(AdminRoles.Editor)]
    public async Task RealCookieMfaPasswordChangeAndRoleBoundaries(string role)
    {
        using var app=Factory();using var scope=app.Services.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();var verifier=scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var totp=scope.ServiceProvider.GetRequiredService<ITotpProvider>();var marker=Guid.NewGuid().ToString("N");var id=Guid.NewGuid().ToString();
        var password="Xy9!"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));var nextPassword="Zz5!"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var roles=role==AdminRoles.Operator?new[]{role,AdminRoles.Moderator}:new[]{role};
        await users.CreateAsync(new(id,"fixture-auth-"+marker,marker+"@example.invalid",verifier.Hash(password),false,null,0,null,null),roles,default);
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            await db.ExecuteAsync("UPDATE admin_users SET must_change_password=TRUE WHERE id=@id",new{id});
            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
            var token=Token(await client.GetStringAsync("/admin/login"));
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/admin/login",Form(token,("userName","fixture-auth-"+marker),("password","incorrect")))).StatusCode);
            await db.ExecuteAsync("UPDATE admin_users SET lockout_end_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 15 MINUTE) WHERE id=@id",new{id});
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/admin/login",Form(token,("userName","fixture-auth-"+marker),("password",password)))).StatusCode);
            await db.ExecuteAsync("UPDATE admin_users SET lockout_end_at=NULL,access_failed_count=0 WHERE id=@id",new{id});
            var login=await client.PostAsync("/admin/login",Form(token,("userName","fixture-auth-"+marker),("password",password)));Assert.Contains("/admin/mfa/enrol",login.Headers.Location!.ToString());
            var enrol=await client.GetStringAsync("/admin/mfa/enrol");var secret=WebUtility.HtmlDecode(Regex.Match(enrol,"class=\"secret\">([^<]+)").Groups[1].Value);Assert.False(string.IsNullOrWhiteSpace(secret));
            var enrolled=await client.PostAsync("/admin/mfa/enrol",Form(Token(enrol),("code",totp.GenerateFor(DateTimeOffset.UtcNow,secret))));
            var diagnostic=await db.ExecuteScalarAsync<string>("SELECT metadata FROM audit_events WHERE action='mfa_enrol_rejected' ORDER BY id DESC LIMIT 1");
            Assert.True(enrolled.Headers.Location!.ToString().EndsWith("/admin/change-password"),diagnostic??"Nenhum motivo de recusa registrado.");
            Assert.Contains("/admin/change-password",(await client.GetAsync("/admin/editorial")).Headers.Location!.ToString());
            Assert.Contains("/admin/change-password",(await client.GetAsync("/api/admin/review/pending")).Headers.Location!.ToString());
            var change=await client.GetStringAsync("/admin/change-password");
            var changed=await client.PostAsync("/admin/change-password",Form(Token(change),("currentPassword",password),("password",nextPassword),("confirmation",nextPassword)));
            Assert.EndsWith("/admin/login",changed.Headers.Location!.ToString());Assert.False((await users.FindByIdAsync(id,default))!.MustChangePassword);
            token=Token(await client.GetStringAsync("/admin/login"));
            login=await client.PostAsync("/admin/login",Form(token,("userName","fixture-auth-"+marker),("password",nextPassword)));Assert.Contains("/admin/mfa/verify",login.Headers.Location!.ToString());
            var verify=await client.GetStringAsync("/admin/mfa/verify");
            var verified=await client.PostAsync("/admin/mfa/verify",Form(Token(verify),("code",totp.GenerateFor(DateTimeOffset.UtcNow.AddSeconds(30),secret))));Assert.Equal(HttpStatusCode.Redirect,verified.StatusCode);
            var editorial=await client.GetAsync("/admin/editorial");Assert.Equal(HttpStatusCode.OK,editorial.StatusCode);
            var moderation=await client.GetAsync("/admin/moderacao");
            if(role==AdminRoles.Editor)Assert.Contains("/admin/access-denied",moderation.Headers.Location!.ToString());else Assert.Equal(HttpStatusCode.OK,moderation.StatusCode);
            var coverage=await client.GetAsync("/admin/cobertura");
            if(role==AdminRoles.Editor)Assert.Contains("/admin/access-denied",coverage.Headers.Location!.ToString());else Assert.Equal(HttpStatusCode.OK,coverage.StatusCode);
            var logout=await client.PostAsync("/admin/logout",Form(Token(await editorial.Content.ReadAsStringAsync())));Assert.Equal(HttpStatusCode.Redirect,logout.StatusCode);
            Assert.Contains("/admin/login",(await client.GetAsync("/admin/editorial")).Headers.Location!.ToString());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM admin_user_roles WHERE user_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM admin_users WHERE id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject=@id",new{id});
        }
    }
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string token,params (string Key,string Value)[] pairs)=>new(pairs.Select(p=>new KeyValuePair<string,string>(p.Key,p.Value)).Append(new("__RequestVerificationToken",token)));
    private static WebApplicationFactory<Program> Factory()=>new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.UseEnvironment("Development").ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=DatabaseEnvironment.ConnectionString()})));
}

public sealed class LocalHomologationTheoryAttribute : TheoryAttribute
{
    public LocalHomologationTheoryAttribute()
    {
        if(!OperatingSystem.IsWindows()||Environment.GetEnvironmentVariable("BELRESTAURANTES_VERIFY_HOMOLOGATION")!="true")
            Skip="Verificação opcional das credenciais iniciais das contas reais não habilitada.";
    }
}
