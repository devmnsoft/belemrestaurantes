using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class CatalogAuditTests
{
    [Fact]
    public async Task WriteCurrentReadOnlyCatalogueAuditWhenRequested()
    {
        var output=Environment.GetEnvironmentVariable("CATALOG_AUDIT_OUTPUT");
        if(output is null)return;
        using var provider=(ServiceProvider)ServiceContainer.Build();
        await using var db=await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        const string coordinates="latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND NOT(ABS(latitude)<0.000001 AND ABS(longitude)<0.000001)";
        async Task<object> Rows(string sql)=>(await db.QueryAsync(sql)).Select(row=>(IDictionary<string,object>)row).ToList();
        var audit=new {
            capturedAt=DateTimeOffset.UtcNow,environment="local development; no production comparison",local=db.DataSource is "localhost" or "127.0.0.1",
            summary=await Rows($"""
                SELECT COUNT(*) total,SUM(status='published') published,SUM(status='pending') pending,
                SUM(status='rejected') rejected,SUM(status='suspended') suspended,
                SUM(status='published' AND catalog_tier='ficha') publishedFicha,
                SUM(status='published' AND catalog_tier='guia') publishedGuia,
                SUM(status='published' AND ({coordinates})) publishedCoordinates,
                SUM(address IS NOT NULL AND TRIM(address)<>'') withAddress,
                SUM(phone IS NOT NULL AND TRIM(phone)<>'') withPhone,
                SUM(photo_url IS NOT NULL AND photo_url<>'' AND photo_license IS NOT NULL AND photo_license<>'') withLicensedPhoto,
                SUM(business_status='permanently_closed') possiblyClosed,
                SUM(EXISTS(SELECT 1 FROM restaurant_schedules s WHERE s.restaurant_id=restaurants.id)) withSchedule
                FROM restaurants
                """),
            stages=await Rows("SELECT status,moderation_stage,COUNT(*) total FROM restaurants GROUP BY status,moderation_stage"),
            cities=await Rows("SELECT city,COUNT(*) total,SUM(status='published') published,SUM(status='pending') pending FROM restaurants GROUP BY city ORDER BY total DESC"),
            categories=await Rows("SELECT category,COUNT(*) total,SUM(status='published') published FROM restaurants GROUP BY category ORDER BY total DESC"),
            duplicateCandidates=await Rows("SELECT COUNT(*) groupsFound,COALESCE(SUM(n),0) recordsInGroups FROM (SELECT COUNT(*) n FROM restaurants WHERE status IN ('pending','published') GROUP BY normalized_city,normalized_name,address HAVING COUNT(*)>1) d"),
            transitValidationDestinations=await Rows("SELECT id,name,city,address,latitude,longitude FROM restaurants WHERE status='published' AND latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 ORDER BY id LIMIT 5"),
            sources=await Rows("SELECT provider,COUNT(DISTINCT restaurant_id) records FROM source_observations GROUP BY provider"),
            candidatesForReview=(await provider.GetRequiredService<MySqlBulkModeration>().ListAsync(new ModerationFilter(City:"Belém",Source:"openstreetmap",Completeness:"eligible"),1,default)).Items.Take(10).Select(r=>new {r.Id,r.Name,r.City,r.Category,r.Address,r.SourceUrl,r.ExternalId}),
            discovery=await Rows("SELECT provider,enabled,COUNT(*) targets FROM discovery_targets GROUP BY provider,enabled")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(audit,new JsonSerializerOptions{WriteIndented=true}));
        Assert.NotNull(audit.summary);
    }
}
