using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
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
public sealed class BulkAndVisitTests
{
    [Fact]
    public async Task OperatorCanCreateDisabledTargetForOfficialMunicipalityAndRejectInvalidCity()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var category="fixture-target-"+Guid.NewGuid().ToString("N");
        try
        {
            var html=await client.GetStringAsync("/admin/operacao?city=Marituba");
            var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var fields=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"city","Município inexistente"},{"category",category},{"provider","openstreetmap"},{"priority","2"},{"refreshMinutes","10080"},{"resultLimit","50"}};
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/admin/operacao/novo-alvo",new FormUrlEncodedContent(fields))).StatusCode);
            fields["city"]="Marituba";
            Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/admin/operacao/novo-alvo",new FormUrlEncodedContent(fields))).StatusCode);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM discovery_targets WHERE category=@category AND city='Marituba' AND enabled=FALSE AND priority=2 AND refresh_minutes=10080 AND result_limit=50",new{category}));
        }
        finally{await db.ExecuteAsync("DELETE FROM discovery_targets WHERE category=@category",new{category});await db.ExecuteAsync("DELETE FROM audit_events WHERE action='discovery.target_upsert' AND metadata LIKE @term",new{term="%"+category+"%"});}
    }
    [Fact]
    public async Task PublishedRestaurantCanPlanVisitAndOperatorCanMaintainProvenancedFare()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var marker="journey-"+Guid.NewGuid().ToString("N");var ids=new List<long>();long sourceId=0;
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task Snapshot(string name,string html){var output=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");if(output is not null){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,name+".html"),html);}}
        try
        {
            var id=await scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>().UpsertDiscoveredAsync(new(marker,"Cuia de homologação "+marker,"Nazaré","Belém","Restaurante","Rua de homologação",-1.45,-48.49,"https://example.org/fixture",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);ids.Add(id);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IModerationRepository>().ApproveAsync(id,"fixture-admin",default));
            var html=await client.GetStringAsync("/cuia-rotas?restaurantId="+id);Assert.Contains("Cuia de homologação",WebUtility.HtmlDecode(html));
            await Snapshot("visit-destination",html);
            var form=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"RestaurantId",id.ToString()},{"Origin","Origem privada de homologação"},{"Mode","transit"},{"Arrival","true"},{"LocalTime",DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm")},{"MaxWalkMeters","1000"},{"Preference","less_walking"}};
            var route=await client.PostAsync("/cuia-rotas/consultar",new FormUrlEncodedContent(form));Assert.Equal(HttpStatusCode.OK,route.StatusCode);
            html=await route.Content.ReadAsStringAsync();Assert.Contains("Google Maps",html);Assert.Contains("Horário não confirmado",WebUtility.HtmlDecode(html));
            var whatsapp=Regex.Match(WebUtility.HtmlDecode(html),"https://wa.me/[^\"]+").Value;Assert.DoesNotContain("Origem privada",Uri.UnescapeDataString(whatsapp));
            await Snapshot("visit-fallback",html);
            html=await client.GetStringAsync("/admin/mobilidade?restaurantId="+id);await Snapshot("admin-hours-edit",html);var token=Token(html);
            var sourceUrl="https://example.org/"+marker;
            var source=await client.PostAsync("/admin/mobilidade/fonte",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"name",marker},{"kind","official_reference"},{"sourceUrl",sourceUrl},{"terms","Fixture de integração, não representa serviço real."},{"enabled","true"},{"expiresAt",DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")}}));Assert.Equal(HttpStatusCode.Redirect,source.StatusCode);
            sourceId=await db.ExecuteScalarAsync<long>("SELECT id FROM mobility_sources WHERE source_url=@sourceUrl",new{sourceUrl});
            var fare=await client.PostAsync("/admin/mobilidade/tarifa",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"sourceId",sourceId.ToString()},{"serviceName",marker},{"mode","bus"},{"amount","4.60"},{"validFrom",DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd")},{"validUntil",DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd")},{"conditions","Somente fixture"},{"confidence","estimated"}}));Assert.Equal(HttpStatusCode.Redirect,fare.StatusCode);
            Assert.Equal(4.60m,await db.ExecuteScalarAsync<decimal>("SELECT amount FROM mobility_fares WHERE source_id=@sourceId",new{sourceId}));
            html=await client.GetStringAsync("/cuia-rotas?restaurantId="+id);Assert.Contains(marker,html);
            await db.ExecuteAsync("UPDATE mobility_sources SET expires_at=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 MINUTE) WHERE id=@sourceId",new{sourceId});
            html=await client.GetStringAsync("/cuia-rotas?restaurantId="+id);Assert.DoesNotContain("Somente fixture",html);
        }
        finally
        {
            if(sourceId>0){await db.ExecuteAsync("DELETE FROM mobility_fares WHERE source_id=@sourceId",new{sourceId});await db.ExecuteAsync("DELETE FROM mobility_sources WHERE id=@sourceId",new{sourceId});}
            await db.ExecuteAsync("DELETE FROM audit_events WHERE metadata LIKE @term AND actor_subject='fixture-admin'",new{term="%"+marker+"%"});
            await Cleanup(db,ids);
        }
    }

    [Fact]
    public async Task BulkHasFixedSelectionRevalidationAuditAndIdempotentResults()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var bulk=scope.ServiceProvider.GetRequiredService<MySqlBulkModeration>();
        var ingest=scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker="bulk-fixture-"+Guid.NewGuid().ToString("N");var ids=new List<long>();string? batchId=null;
        try
        {
            for(var i=0;i<3;i++)ids.Add(await ingest.UpsertDiscoveredAsync(new(marker+i,marker+i,null,"Belém","Restaurante",i==2?null:"Rua de homologação "+i,-1.45,-48.49,"https://example.org/source",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default));
            var list=await bulk.ListAsync(new(Term:marker),1,default);Assert.Equal(3,list.TotalCount);
            var batch=await bulk.PrepareAsync("fixture-admin","approve",null,[],true,new(Term:marker),default);batchId=batch.Id;
            await db.ExecuteAsync("UPDATE restaurants SET address=NULL WHERE id=@id",new{id=ids[1]});
            Assert.Null(await bulk.ExecuteAsync(batch.Id,"another-actor",default));
            var result=await bulk.ExecuteAsync(batch.Id,"fixture-admin",default);
            Assert.Single(result!.Results!,r=>r.Outcome=="applied");Assert.Equal(2,result.Results!.Count(r=>r.Outcome=="ignored"));
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IRestaurantReadRepository>().GetPublishedAsync(ids[0],default));
            var replay=await bulk.ExecuteAsync(batch.Id,"fixture-admin",default);Assert.Equal(result.Results,replay!.Results);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_events WHERE action='restaurant.bulk_approve' AND entity_id IN @ids",new{ids}));
        }
        finally {if(batchId is not null)await db.ExecuteAsync("DELETE FROM moderation_batches WHERE id=@batchId",new{batchId});await Cleanup(db,ids);}
    }
    [Fact]
    public async Task ConfirmedSchedulePersistsExceptionsHistoryAndOwnerPriority()
    {
        using var app=Factory();using var scope=app.Services.CreateScope();
        var marker=Guid.NewGuid().ToString("N");
        var ingest=scope.ServiceProvider.GetRequiredService<IRestaurantIngestionRepository>();
        var id=await ingest.UpsertDiscoveredAsync(new("hours-fixture-"+marker,"Cuia Homologação "+marker,null,"Belém","Restaurante","Rua teste",null,null,"https://example.org",null,null,null,[]),"test_fixture",DateTimeOffset.UtcNow,default);
        var hours=scope.ServiceProvider.GetRequiredService<IOperatingHoursRepository>();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            var info=new OperationInfo{Authority="owner",SourceUrl="https://example.org/hours",ConfirmedAt=DateTime.UtcNow,ExpiresAt=DateTime.UtcNow.AddDays(30)};
            await hours.SaveAsync(id,0,info,[new(1,false,false,[new(660,840),new(1080,1380)]),new(2,true,false,[],DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))],"fixture-owner",default);
            var stored=await hours.GetAsync(id,default);Assert.Equal(2,stored.Days[0].Windows.Count);Assert.Equal(1,stored.Info!.Version);
            await Assert.ThrowsAsync<ArgumentException>(()=>hours.SaveAsync(id,0,info,stored.Days,"fixture-owner",default));
            info.Authority="external";await Assert.ThrowsAsync<ArgumentException>(()=>hours.SaveAsync(id,1,info,stored.Days,"fixture-source",default));
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurant_operation_history WHERE restaurant_id=@id",new{id}));
        }
        finally{await Cleanup(db,[id]);}
    }
    [Theory]
    [InlineData("/admin/receitas")][InlineData("/admin/moderacao")][InlineData("/admin/operacao")][InlineData("/admin/mobilidade")][InlineData("/admin/editorial")][InlineData("/admin/usuarios")][InlineData("/cuia-rotas")][InlineData("/qual-onibus")][InlineData("/sabores")][InlineData("/privacidade")][InlineData("/termos")]
    public async Task NewViewsRenderAndCaptureEvidence(string path)
    {
        using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        using(var scope=app.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var response=await client.GetAsync(path);var html=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,html[..Math.Min(html.Length,2000)]);
        var output=Environment.GetEnvironmentVariable("BELRESTAURANTES_VIEW_SNAPSHOTS");
        if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,path.Trim('/').Replace('/','-')+".html"),html);}
    }
    [Fact] public async Task BulkAndHoursRequireAntiforgery()
    {using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});foreach(var path in new[]{"/admin/moderacao/confirmar","/admin/mobilidade/horarios","/cuia-rotas/consultar","/admin/receitas/salvar","/admin/operacao/manutencao","/admin/operacao/enriquecer","/admin/operacao/plano-estadual"})Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(path,new FormUrlEncodedContent([]))).StatusCode);}
    [Theory]
    [InlineData(AdminRoles.Editor,"/admin/moderacao")][InlineData(AdminRoles.Editor,"/admin/operacao")][InlineData(AdminRoles.Editor,"/admin/usuarios")]
    [InlineData(AdminRoles.Operator,"/admin/moderacao")][InlineData(AdminRoles.Operator,"/admin/usuarios")][InlineData(AdminRoles.Moderator,"/admin/operacao")][InlineData(AdminRoles.Moderator,"/admin/usuarios")]
    public async Task RoleWithoutPermissionCannotEnter(string role,string path)
    {using var app=Factory();using var client=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role",role);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);}
    private static async Task Cleanup(MySqlConnector.MySqlConnection db,IEnumerable<long> ids)
    {
        if(!ids.Any())return;
        await db.ExecuteAsync("DELETE FROM source_observations WHERE restaurant_id IN @ids",new{ids});
        await db.ExecuteAsync("DELETE FROM moderation_queue WHERE item_type='restaurant' AND item_id IN @ids",new{ids});
        await db.ExecuteAsync("DELETE FROM audit_events WHERE entity_type='restaurant' AND entity_id IN @ids AND actor_subject LIKE 'fixture%'",new{ids});
        await db.ExecuteAsync("DELETE FROM restaurants WHERE id IN @ids",new{ids});
    }
    internal static WebApplicationFactory<Program> Factory()=>new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.UseEnvironment("Development")
        .ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=DatabaseEnvironment.ConnectionString()}))
        .ConfigureServices(services=>services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="bulk-test";o.DefaultChallengeScheme="bulk-test";}).AddScheme<AuthenticationSchemeOptions,RoleHandler>("bulk-test",_=>{})));
    public sealed class RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {var role=Request.Headers["Test-Role"].FirstOrDefault()??AdminRoles.PlatformAdmin;return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,"fixture-admin"),new(ClaimTypes.Name,"fixture-admin"),new(ClaimTypes.Role,role),new("amr","mfa")],Scheme.Name)),Scheme.Name)));}
    }
}
