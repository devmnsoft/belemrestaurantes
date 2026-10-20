using System.Net;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class PartnerJourneyTests
{
    [Fact]
    public async Task OwnerPagesAndPublicMenuUseThePublishedReadModel()
    {
        using var app=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{
            b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=DatabaseEnvironment.ConnectionString()}));
            b.ConfigureServices(s=>{
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions,MemberFixture>("s45-member",_=>{});
                s.PostConfigure<AuthenticationOptions>(o=>o.Schemes.Single(s=>s.Name==MemberAuth.Scheme).HandlerType=typeof(MemberFixture));
            });
        });
        using var scope=app.Services.CreateScope();await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker="partner-s45-"+Guid.NewGuid().ToString("N");
        var id=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new(marker,"TESTE SIMULADO — Parceiro",null,"Belém","Restaurante","Rua de teste",-1.45,-48.49,"https://example.org/test",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
        try {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(id,"test-fixture",default));
            await db.ExecuteAsync("INSERT INTO restaurant_owners(restaurant_id,user_id) VALUES(@id,'s45-member')",new{id});
            using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
            string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var menu=await client.GetStringAsync($"/partner/{id}/menu");
            var createCategory=await client.PostAsync($"/partner/{id}/menu/category",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Token(menu)},{"name","Categoria de teste"},{"displayOrder","1"}}));
            Assert.Equal(HttpStatusCode.Redirect,createCategory.StatusCode);
            foreach(var (name,route) in new[]{("menu-public",$"/cardapio/{id}"),("menu-manage",$"/partner/{id}/menu"),("partner-claim",$"/partner/claim/{id}"),("partner-manage",$"/partner/{id}"),("partner-schedule",$"/partner/{id}/schedule"),("partner-announcements",$"/partner/{id}/announcements"),("partner-stats",$"/partner/{id}/stats"),("partner-plans",$"/partner/{id}/plans")}) {
                using var response=await client.GetAsync(route);var html=await response.Content.ReadAsStringAsync();
                if(response.StatusCode!=HttpStatusCode.OK){var folder=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(folder is not null)await File.WriteAllTextAsync(Path.Combine(folder,"error-"+name+".html"),html);}
                Assert.True(response.StatusCode==HttpStatusCode.OK,route+" returned "+response.StatusCode);
                if(name=="menu-public"){Assert.Contains("<dialog",html);Assert.DoesNotContain("onclick=",html);Assert.Contains("data-menu-code-image data-src",html);}
                var output=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(output is not null){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,name+".html"),html);}
            }
            var manage=await client.GetStringAsync($"/partner/{id}");
            var save=await client.PostAsync($"/partner/{id}/profile",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Token(manage)},{"Slogan","Slogan de teste"},{"OfficialDescription","Conteúdo sintético de integração"}}));
            Assert.Equal(HttpStatusCode.Redirect,save.StatusCode);
            Assert.Contains("Slogan de teste",await client.GetStringAsync($"/partner/{id}"));
        } finally {
            await db.ExecuteAsync("DELETE FROM restaurant_owners WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM restaurant_stats WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id});
        }
    }
    public sealed class MemberFixture(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,"s45-member"),new(ClaimTypes.Name,"Parceiro de teste"),new("email_verified","true")],Scheme.Name)),Scheme.Name)));
    }
}
