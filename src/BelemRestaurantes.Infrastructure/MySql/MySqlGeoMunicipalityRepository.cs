using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlGeoMunicipalityRepository(MySqlConnectionFactory factory) : IGeoMunicipalityRepository
{
    public async Task<IReadOnlyList<GeoMunicipality>> ListAllAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   ibge_code AS IbgeCode,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   mesoregion AS Mesoregion,
                   microregion AS Microregion,
                   is_tourist_pole AS IsTouristPole,
                   priority_level AS PriorityLevel,
                   last_scanned_at AS LastScannedAt,
                   coverage_status AS CoverageStatus,
                   created_at AS CreatedAt
            FROM geo_municipalities
            ORDER BY priority_level ASC, name ASC
            """;

        var list = await connection.QueryAsync<GeoMunicipality>(new CommandDefinition(sql, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<IReadOnlyList<MunicipalityCoverageStats>> GetCoverageStatsAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT m.ibge_code AS IbgeCode,
                   m.name AS MunicipalityName,
                   m.mesoregion AS Mesoregion,
                   m.is_tourist_pole AS IsTouristPole,
                   m.coverage_status AS CoverageStatus,
                   m.last_scanned_at AS LastScannedAt,
                   COUNT(r.id) AS TotalDiscovered,
                   COUNT(CASE WHEN r.status = 'published' THEN 1 END) AS TotalApproved,
                   COUNT(CASE WHEN r.status = 'pending' THEN 1 END) AS TotalPending,
                   COUNT(CASE WHEN (r.photo_url IS NULL OR r.photo_url = '') THEN 1 END) AS TotalWithoutPhotos,
                   COUNT(CASE WHEN r.updated_at < DATE_SUB(NOW(), INTERVAL 90 DAY) THEN 1 END) AS TotalStale
            FROM geo_municipalities m
            LEFT JOIN restaurants r ON LOWER(r.city) = LOWER(m.name)
            GROUP BY m.id, m.ibge_code, m.name, m.mesoregion, m.is_tourist_pole, m.coverage_status, m.last_scanned_at
            ORDER BY m.priority_level ASC, TotalDiscovered DESC, m.name ASC
            """;

        var list = await connection.QueryAsync<MunicipalityCoverageStats>(new CommandDefinition(sql, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<GeoMunicipality?> GetByIbgeCodeAsync(uint ibgeCode, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   ibge_code AS IbgeCode,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   mesoregion AS Mesoregion,
                   microregion AS Microregion,
                   is_tourist_pole AS IsTouristPole,
                   priority_level AS PriorityLevel,
                   last_scanned_at AS LastScannedAt,
                   coverage_status AS CoverageStatus,
                   created_at AS CreatedAt
            FROM geo_municipalities
            WHERE ibge_code = @ibgeCode
            LIMIT 1
            """;

        return await connection.QueryFirstOrDefaultAsync<GeoMunicipality>(
            new CommandDefinition(sql, new { ibgeCode }, cancellationToken: ct));
    }

    public async Task<GeoMunicipality?> GetByNameAsync(string name, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   ibge_code AS IbgeCode,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   mesoregion AS Mesoregion,
                   microregion AS Microregion,
                   is_tourist_pole AS IsTouristPole,
                   priority_level AS PriorityLevel,
                   last_scanned_at AS LastScannedAt,
                   coverage_status AS CoverageStatus,
                   created_at AS CreatedAt
            FROM geo_municipalities
            WHERE LOWER(name) = LOWER(@name) OR LOWER(normalized_name) = LOWER(@name)
            LIMIT 1
            """;

        return await connection.QueryFirstOrDefaultAsync<GeoMunicipality>(
            new CommandDefinition(sql, new { name = name.Trim() }, cancellationToken: ct));
    }

    public async Task UpdateScanTimestampAsync(uint ibgeCode, DateTimeOffset scannedAt, string status, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE geo_municipalities
            SET last_scanned_at = @scannedAt,
                coverage_status = @status
            WHERE ibge_code = @ibgeCode
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { ibgeCode, scannedAt = scannedAt.UtcDateTime, status }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GeoMunicipality>> GetPriorityUnscannedAsync(int limit, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id AS Id,
                   ibge_code AS IbgeCode,
                   name AS Name,
                   normalized_name AS NormalizedName,
                   mesoregion AS Mesoregion,
                   microregion AS Microregion,
                   is_tourist_pole AS IsTouristPole,
                   priority_level AS PriorityLevel,
                   last_scanned_at AS LastScannedAt,
                   coverage_status AS CoverageStatus,
                   created_at AS CreatedAt
            FROM geo_municipalities
            ORDER BY priority_level ASC, (last_scanned_at IS NULL) DESC, last_scanned_at ASC
            LIMIT @limit
            """;

        var list = await connection.QueryAsync<GeoMunicipality>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return list.AsList();
    }
}
