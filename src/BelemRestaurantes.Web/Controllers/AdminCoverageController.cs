using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Web.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="PlatformOperator"), Route("admin/cobertura")]
public sealed class AdminCoverageController(MySqlConnectionFactory factory):Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct, [FromQuery] string? region=null, [FromQuery] string? q=null)
    {
        await using var connection=await factory.OpenAsync(ct);
        // Agrupa no banco: não carrega nomes, telefones ou 100 mil entidades na memória da página.
        var rows=(await connection.QueryAsync<CoverageCounts>(new CommandDefinition("""
            SELECT normalized_city AS CityKey, COUNT(*) AS Total,
                   SUM(status='published') AS Published, SUM(status='pending') AS Pending,
                   SUM(address IS NULL OR TRIM(address)='') AS MissingAddress,
                   SUM(latitude IS NULL OR longitude IS NULL) AS MissingCoordinates
            FROM restaurants GROUP BY normalized_city
            """,cancellationToken:ct))).ToDictionary(row=>row.CityKey,StringComparer.OrdinalIgnoreCase);
        var categories=(await connection.QueryAsync<CoverageCategory>(new CommandDefinition("""
            SELECT COALESCE(NULLIF(category,''),'Sem categoria') AS Name, COUNT(*) AS Total,
                   SUM(status='published') AS Published
            FROM restaurants WHERE normalized_city IN @cityKeys GROUP BY category ORDER BY Total DESC, Name LIMIT 40
            """,new{cityKeys=ParaOfficialCatalog.Municipalities.Select(city=>city.NormalizedName).ToArray()},cancellationToken:ct))).AsList();
        var all=ParaOfficialCatalog.Municipalities.Select(city=>new CoverageCity(city,
            rows.GetValueOrDefault(city.NormalizedName)??new CoverageCounts{CityKey=city.NormalizedName})).ToArray();
        var term=string.IsNullOrWhiteSpace(q)?null:q.Trim()[..Math.Min(q.Trim().Length,120)];
        var selected=all.Where(city=>(string.IsNullOrWhiteSpace(region)||city.Municipality.Mesoregion==region)
            && (term is null || NameNormalizer.Normalize(city.Municipality.Name).Contains(NameNormalizer.Normalize(term),StringComparison.Ordinal))).ToArray();
        var unknown=rows.Where(row=>!ParaOfficialCatalog.Municipalities.Any(city=>city.NormalizedName==row.Key)).Sum(row=>row.Value.Total);
        return View(new CoverageView(all,selected,categories,region,term,unknown,DateTimeOffset.UtcNow));
    }
}
