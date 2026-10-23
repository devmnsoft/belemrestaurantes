using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Members;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint54AccessSearchTests
{
    private static WebApplicationFactory<Program> Factory(string environment="Development")=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment(environment).ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=environment=="Production"?new MySqlConnector.MySqlConnectionStringBuilder(DatabaseEnvironment.ConnectionString()!){SslMode=MySqlConnector.MySqlSslMode.VerifyFull}.ConnectionString:DatabaseEnvironment.ConnectionString(),["MailEncryption:KeyRingPath"]=DatabaseEnvironment.MailKeys,["Development:LocalMailboxEnabled"]="true",["Database:ApplyMigrations"]=environment=="Production"?"false":"true"})));
    [Fact]public async Task AdminEmailLoginAndSingleUseRecoveryPreserveMfa()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();var sp=scope.ServiceProvider;await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var users=sp.GetRequiredService<IAdminUserRepository>();var passwords=sp.GetRequiredService<IPasswordVerifier>();var id=Guid.NewGuid().ToString("N");var email=id+"@example.invalid";var name="fixture54-"+id;var password="Vv8!"+Guid.NewGuid().ToString("N");var next="Yy6!"+Guid.NewGuid().ToString("N");
        var secret=sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Admin.Totp.v1").Protect(System.Text.Encoding.UTF8.GetBytes(sp.GetRequiredService<ITotpProvider>().GenerateSecret()));
        await users.CreateAsync(new(id,name,email,passwords.Hash(password),true,secret,0,null,null),[AdminRoles.PlatformAdmin],default);await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            Assert.Equal(id,(await users.FindByNameAsync(" "+email+" ",default))!.Id);
            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});var html=await client.GetStringAsync("/admin/login");var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var login=await client.PostAsync("/admin/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"userName"," "+email+" "},{"password",password}}));Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);Assert.Contains("/admin/mfa/verify",login.Headers.Location!.ToString());
            var recovery=sp.GetRequiredService<IAdminPasswordRecovery>();await recovery.RequestAsync(email,default);
            var protector=sp.GetRequiredService<MailPayloadProtector>();var payloads=await db.QueryAsync<byte[]>("SELECT payload FROM transactional_email_outbox ORDER BY created_at DESC LIMIT 30");var mail=payloads.Select(p=>{try{return protector.Unprotect(p);}catch{return null;}}).First(p=>p?.Recipient==email)!;var reset=new Uri(mail.Link!).Fragment[7..];
            Assert.True(await recovery.ResetAsync(reset,next,default));Assert.False(await recovery.ResetAsync(reset,password,default));
            var updated=await users.FindByIdAsync(id,default);Assert.True(updated!.TwoFactorEnabled);Assert.Equal(secret,updated.TwoFactorSecret);Assert.True(passwords.Verify(next,updated.PasswordHash));Assert.False(passwords.Verify(password,updated.PasswordHash));
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/dev/caixa-de-entrada")).StatusCode);
        }finally{await db.ExecuteAsync("DELETE FROM admin_password_tokens WHERE user_id=@id;DELETE FROM admin_user_roles WHERE user_id=@id;DELETE FROM admin_users WHERE id=@id;DELETE FROM audit_events WHERE actor_subject=@id OR (action IN ('admin.password_reset_requested','admin.password_reset') AND JSON_UNQUOTE(JSON_EXTRACT(metadata,'$.userId'))=@id)",new{id});}
    }
    [Fact]public async Task PersistedPlaceDescriptionsExplainAmbiguityWithoutCloudAndBrandIsCuia()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();var sp=scope.ServiceProvider;await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);var content=sp.GetRequiredService<IExternalContent>();var name="fixture54-"+Guid.NewGuid().ToString("N");var ids=new List<long>();await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            foreach(var city in new[]{"Marituba","Altamira"})ids.Add((await content.ObserveAsync(new(){Title=name+" Restaurante e cultura",Url="https://example.org/"+name+"/"+city,City=city,FactsJson=JsonSerializer.Serialize(new ExternalContentFacts(name,Description:"Referência cultural em "+city+" documentada pela fonte."))},default))!.Value);
            var response=await sp.GetRequiredService<ICuiaAiService>().AskAsync(new("O que era "+name+"?",SearchWeb:false),default);Assert.Equal("reference_answer",response.GenerationStatus);Assert.Contains("Marituba",response.AnswerMarkdown);Assert.Contains("Altamira",response.AnswerMarkdown);Assert.NotEmpty(response.SuggestedFollowUps);
            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});var status=await client.GetStringAsync("/api/cuia-ia/status");Assert.DoesNotContain("Drael",status);Assert.DoesNotContain("providers",status);Assert.Contains("Cuia",status);
        }finally{foreach(var id in ids)await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='external_content' AND entity_id=@id;DELETE FROM external_content WHERE id=@id",new{id});}
    }
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    [Fact]public async Task NewMemberRegistersConfirmsAndLogsInByCpfAndEmailWithoutAdminRights()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();var sp=scope.ServiceProvider;await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var email="fixture54-member-"+Guid.NewGuid().ToString("N")+"@example.invalid";var password="Qz7!"+Guid.NewGuid().ToString("N");var cpf=Sprint48IdentityTests.NewCpf();string? id=null;
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        async Task<HttpResponseMessage> Post(string url,Dictionary<string,string> fields,string? get=null){fields["__RequestVerificationToken"]=Token(await client.GetStringAsync(get??url));return await client.PostAsync(url,new FormUrlEncodedContent(fields));}
        try{
            var registered=await Post("/account/register",new(){{"FullName","Teste cadastro completo"},{"Email",email},{"Cpf",cpf},{"Password",password},{"ConfirmPassword",password},{"AcceptTerms","true"}});Assert.Equal(HttpStatusCode.OK,registered.StatusCode);
            id=await db.QuerySingleAsync<string>("SELECT id FROM member_users WHERE email=@email",new{email});
            Assert.Equal(HttpStatusCode.OK,(await Post("/account/login",new(){{"Email",email},{"Password",password}})).StatusCode);
            var protector=sp.GetRequiredService<MailPayloadProtector>();var payloads=await db.QueryAsync<byte[]>("SELECT payload FROM transactional_email_outbox ORDER BY created_at DESC LIMIT 30");var mail=payloads.Select(p=>{try{return protector.Unprotect(p);}catch{return null;}}).First(p=>p?.Recipient==email)!;
            var confirmed=await Post("/account/confirm",new(){{"token",new Uri(mail.Link!).Fragment[7..]}});Assert.Equal(HttpStatusCode.OK,confirmed.StatusCode);
            var login=await Post("/account/login",new(){{"Email",cpf},{"Password",password}});Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);Assert.Equal("/account",login.Headers.Location!.ToString());Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/account")).StatusCode);
            var admin=await client.GetAsync("/admin/dashboard");Assert.Equal(HttpStatusCode.Redirect,admin.StatusCode);Assert.Contains("/admin/login",admin.Headers.Location!.ToString());
            Assert.Equal(HttpStatusCode.Redirect,(await Post("/account/logout",new(),"/account")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect,(await Post("/account/login",new(){{"Email"," "+email+" "},{"Password",password}})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/account")).StatusCode);
        }finally{if(id is not null)await db.ExecuteAsync("DELETE FROM audit_events WHERE actor_subject=@id",new{id});await db.ExecuteAsync("DELETE FROM member_users WHERE email=@email",new{email});}
    }
    [Fact]public async Task LocalInboxIsLoopbackOnlyNotProductionAndSkipsClearedPayloads()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);var id=Guid.NewGuid().ToString("N");
        await db.ExecuteAsync("INSERT INTO transactional_email_outbox(id,payload,next_attempt_at,created_at,expires_at) VALUES(@id,NULL,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE))",new{id});
        try{
            async Task<int> Get(WebApplicationFactory<Program> factory,string host,IPAddress ip){var context=await factory.Server.SendAsync(ctx=>{ctx.Request.Scheme="https";ctx.Request.Method="GET";ctx.Request.Path="/dev/caixa-de-entrada";ctx.Request.Host=new(host);ctx.Connection.RemoteIpAddress=ip;});return context.Response.StatusCode;}
            Assert.Equal(200,await Get(app,"localhost",IPAddress.Loopback));Assert.Equal(404,await Get(app,"evil.example",IPAddress.Loopback));Assert.Equal(404,await Get(app,"localhost",IPAddress.Parse("203.0.113.10")));
            using var production=Factory("Production");Assert.Equal(404,await Get(production,"localhost",IPAddress.Loopback));
        }finally{await db.ExecuteAsync("DELETE FROM transactional_email_outbox WHERE id=@id",new{id});}
    }
}
