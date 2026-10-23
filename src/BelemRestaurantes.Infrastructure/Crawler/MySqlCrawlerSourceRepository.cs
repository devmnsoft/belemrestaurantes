using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class MySqlCrawlerSourceRepository : ICrawlerSourceRepository
{
    private readonly MySqlConnectionFactory _factory;

    public MySqlCrawlerSourceRepository(MySqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<CrawlerSource>> ListSourcesAsync(string? status, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, base_url AS BaseUrl, source_type AS SourceType, content_only AS ContentOnly, target_city AS TargetCity,
                   default_category AS DefaultCategory, status, robots_txt_status AS RobotsTxtStatus,
                   crawl_delay_seconds AS CrawlDelaySeconds, last_crawled_at AS LastCrawledAt,
                   last_success_at AS LastSuccessAt, last_error AS LastError, etag AS ETag,
                   last_modified_header AS LastModifiedHeader, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM crawler_sources
            WHERE (@Status IS NULL OR status = @Status)
            ORDER BY id ASC;
            """;

        var list = await connection.QueryAsync<CrawlerSource>(new CommandDefinition(sql, new { Status = status }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<CrawlerSource?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, base_url AS BaseUrl, source_type AS SourceType, content_only AS ContentOnly, target_city AS TargetCity,
                   default_category AS DefaultCategory, status, robots_txt_status AS RobotsTxtStatus,
                   crawl_delay_seconds AS CrawlDelaySeconds, last_crawled_at AS LastCrawledAt,
                   last_success_at AS LastSuccessAt, last_error AS LastError, etag AS ETag,
                   last_modified_header AS LastModifiedHeader, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM crawler_sources
            WHERE id = @Id;
            """;

        return await connection.QueryFirstOrDefaultAsync<CrawlerSource>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<CrawlerSource?> GetByBaseUrlAsync(string baseUrl, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, base_url AS BaseUrl, source_type AS SourceType, content_only AS ContentOnly, target_city AS TargetCity,
                   default_category AS DefaultCategory, status, robots_txt_status AS RobotsTxtStatus,
                   crawl_delay_seconds AS CrawlDelaySeconds, last_crawled_at AS LastCrawledAt,
                   last_success_at AS LastSuccessAt, last_error AS LastError, etag AS ETag,
                   last_modified_header AS LastModifiedHeader, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM crawler_sources
            WHERE base_url = @BaseUrl;
            """;

        return await connection.QueryFirstOrDefaultAsync<CrawlerSource>(new CommandDefinition(sql, new { BaseUrl = baseUrl }, cancellationToken: ct));
    }

    public async Task<long> CreateSourceAsync(CrawlerSource source, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            INSERT INTO crawler_sources (name, base_url, source_type, target_city, default_category, status, crawl_delay_seconds,content_only)
            VALUES (@Name, @BaseUrl, @SourceType, @TargetCity, @DefaultCategory, @Status, @CrawlDelaySeconds,@ContentOnly);
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            new
            {
                source.Name,
                source.BaseUrl,
                source.SourceType,
                source.TargetCity,
                source.DefaultCategory,
                source.Status,
                source.CrawlDelaySeconds,
                source.ContentOnly
            },
            cancellationToken: ct));
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_sources
            SET status = @Status
            WHERE id = @Id;
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Status = status }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> UpdateRobotsStatusAsync(long id, string robotsStatus, int crawlDelay, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_sources
            SET robots_txt_status = @RobotsStatus,
                crawl_delay_seconds = @CrawlDelay
            WHERE id = @Id;
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, RobotsStatus = robotsStatus, CrawlDelay = crawlDelay },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task RecordCrawlResultAsync(long id, bool success, string? error, string? etag, string? lastModified, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);

        const string sql = """
            UPDATE crawler_sources
            SET last_crawled_at = CURRENT_TIMESTAMP(6),
                last_success_at = CASE WHEN @Success = 1 THEN CURRENT_TIMESTAMP(6) ELSE last_success_at END,
                last_error = @Error,
                etag = COALESCE(@ETag, etag),
                last_modified_header = COALESCE(@LastModified, last_modified_header)
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Success = success ? 1 : 0, Error = error, ETag = etag, LastModified = lastModified },
            cancellationToken: ct));
    }
}
