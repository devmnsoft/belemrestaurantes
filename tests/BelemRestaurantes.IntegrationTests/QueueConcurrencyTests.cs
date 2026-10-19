using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class QueueConcurrencyTests
{
    [Fact]
    public async Task ProviderOutageDefersRunningJobWithoutConsumingAttempts()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var connection = await provider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var marker = "outage-" + Guid.NewGuid().ToString("N");
        var targetId = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO discovery_targets(city, normalized_city, category, enabled, next_run_at)
            VALUES ('Maracanã', @marker, 'regional', TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """, new { marker });
        try
        {
            var jobId = await connection.ExecuteScalarAsync<long>("""
                INSERT INTO discovery_jobs(target_id,city,category,state,attempts,locked_by,locked_until)
                VALUES(@targetId,'Maracanã','regional','running',2,'test-outage',DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 4 MINUTE));
                SELECT LAST_INSERT_ID();
                """, new { targetId });
            var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
            await provider.GetRequiredService<IDiscoveryQueue>().DeferAsync(jobId, "HTTP 502", retryAt, default);
            var row = await connection.QuerySingleAsync("SELECT state, attempts, next_attempt_at, locked_by, locked_until, last_error FROM discovery_jobs WHERE id=@jobId", new { jobId });
            Assert.Equal("pending", (string)row.state);
            Assert.Equal(2, (int)row.attempts);
            Assert.True((DateTime)row.next_attempt_at > DateTime.UtcNow.AddMinutes(1));
            Assert.Null((object?)row.locked_by);
            Assert.Null((object?)row.locked_until);
            Assert.Equal("HTTP 502", (string)row.last_error);
        }
        finally
        {
            await connection.ExecuteAsync("DELETE FROM discovery_jobs WHERE target_id=@targetId", new { targetId });
            await connection.ExecuteAsync("DELETE FROM discovery_targets WHERE id=@targetId", new { targetId });
        }
    }

    [Fact]
    public async Task Concurrent_claims_are_distinct_failed_jobs_retry_then_reach_dead_letter_and_stale_jobs_recover()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        await provider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var factory = provider.GetRequiredService<MySqlConnectionFactory>();
        var queue = provider.GetRequiredService<IDiscoveryQueue>();
        await using var connection = await factory.OpenAsync(default);
        var marker = "test-" + Guid.NewGuid().ToString("N");
        var ids = new List<long>();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var id = await connection.ExecuteScalarAsync<long>("""
                    INSERT INTO discovery_targets(city, normalized_city, category, enabled, next_run_at)
                    VALUES ('Belém', @normalized, 'restaurante', TRUE, UTC_TIMESTAMP(6));
                    SELECT LAST_INSERT_ID();
                    """, new { normalized = marker + i });
                ids.Add(id);
                await connection.ExecuteAsync("INSERT INTO discovery_jobs(target_id,city,category) VALUES(@id,'Belém','restaurante')", new { id });
            }
            var claims = await Task.WhenAll(queue.ClaimDueAsync(1, "test-a", TimeSpan.FromMinutes(4), default),
                queue.ClaimDueAsync(1, "test-b", TimeSpan.FromMinutes(4), default));
            var claimedJobs = claims.SelectMany(items => items).ToList();
            Assert.NotEmpty(claimedJobs);
            Assert.Equal(claimedJobs.Count, claimedJobs.Select(job => job.JobId).Distinct().Count());
            if (claimedJobs.Count < 2)
                claimedJobs.AddRange(await queue.ClaimDueAsync(1, "test-after-contention", TimeSpan.FromMinutes(4), default));
            var jobs = claimedJobs.ToArray();
            Assert.Equal(2, jobs.Length);
            Assert.Equal(2, jobs.Select(job => job.JobId).Distinct().Count());
            var jobId = jobs[0].JobId;
            await queue.FailAsync(jobId, "simulated", TimeSpan.FromMinutes(1), 3, default);
            Assert.Equal("pending", await connection.ExecuteScalarAsync<string>("SELECT state FROM discovery_jobs WHERE id=@jobId", new { jobId }));
            await queue.FailAsync(jobId, "simulated", TimeSpan.FromMinutes(2), 3, default);
            await queue.FailAsync(jobId, "simulated", TimeSpan.FromMinutes(4), 3, default);
            Assert.Equal("dead_lettered", await connection.ExecuteScalarAsync<string>("SELECT state FROM discovery_jobs WHERE id=@jobId", new { jobId }));
            await connection.ExecuteAsync("UPDATE discovery_jobs SET locked_until=DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 MINUTE) WHERE id=@id", new { id = jobs[1].JobId });
            var staleBefore = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM discovery_jobs WHERE state='running' AND (locked_until IS NULL OR locked_until <= UTC_TIMESTAMP(6))");
            Assert.Equal(staleBefore, await queue.ReleaseStaleLocksAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal("pending", await connection.ExecuteScalarAsync<string>("SELECT state FROM discovery_jobs WHERE id=@id", new { id = jobs[1].JobId }));
            var recovered = await queue.ClaimDueAsync(1, "test-restart", TimeSpan.FromMinutes(4), default);
            Assert.Equal(jobs[1].JobId, Assert.Single(recovered).JobId);
            await queue.CompleteAsync(recovered[0].JobId, default);
            var due = await connection.ExecuteScalarAsync<DateTime>("SELECT next_run_at FROM discovery_targets WHERE id=@id", new { id = recovered[0].TargetId });
            Assert.True(due > DateTime.UtcNow.AddDays(6));
        }
        finally
        {
            if (ids.Count > 0)
            {
                await connection.ExecuteAsync("DELETE FROM discovery_jobs WHERE target_id IN @ids", new { ids });
                await connection.ExecuteAsync("DELETE FROM discovery_targets WHERE id IN @ids", new { ids });
            }
        }
    }

    [Fact]
    public async Task Concurrent_migration_runners_serialize_and_preserve_checksums()
    {
        using var provider = (ServiceProvider)ServiceContainer.Build();
        var runner = provider.GetRequiredService<MigrationRunner>();
        var results = await Task.WhenAll(runner.ApplyAsync(default), runner.ApplyAsync(default));
        Assert.All(results, result => Assert.Equal(MigrationRunner.LoadScripts().Count, result.Applied + result.AlreadyCurrent.Count));
        var current = await runner.ApplyAsync(default);
        Assert.Equal(0, current.Applied);
    }
}
