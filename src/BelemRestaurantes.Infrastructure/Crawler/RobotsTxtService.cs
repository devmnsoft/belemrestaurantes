using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class RobotsTxtService : IRobotsTxtService
{
    private readonly ISafeHttpClient _httpClient;
    private readonly ILogger<RobotsTxtService> _logger;
    private static readonly ConcurrentDictionary<string, CachedRobotsEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public RobotsTxtService(ISafeHttpClient httpClient, ILogger<RobotsTxtService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(bool IsAllowed, int CrawlDelaySeconds)> CheckAccessAsync(
        string url,
        string userAgent = "CuiaCrawler",
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (false, 0);
        }

        var hostKey = $"{uri.Scheme}://{uri.Authority}";

        if (Cache.TryGetValue(hostKey, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return (IsPathAllowed(entry.Rules, uri.AbsolutePath), entry.CrawlDelaySeconds);
        }

        var robotsUrl = $"{hostKey}/robots.txt";
        var result = await _httpClient.DownloadAsync(robotsUrl, timeoutSeconds: 8, ct: ct);

        if(!result.Success && result.StatusCode is not (404 or 410))
        {
            // A timeout, access denial or unavailable server cannot establish permission.
            Cache[hostKey]=new CachedRobotsEntry(["/"],60,DateTimeOffset.UtcNow.AddMinutes(5));
            return (false,60);
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.ContentText))
        {
            // Ausência explícita (404/410) ou arquivo vazio válido: ritmo conservador.
            var defaultEntry = new CachedRobotsEntry(
                Rules: [],
                CrawlDelaySeconds: 5,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(12)
            );
            Cache[hostKey] = defaultEntry;
            return (true, 5);
        }

        var parsed = ParseRobotsTxt(result.ContentText, userAgent);
        var cached = new CachedRobotsEntry(
            Rules: parsed.DisallowedPaths,
            CrawlDelaySeconds: parsed.CrawlDelaySeconds,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(24)
        );

        Cache[hostKey] = cached;
        return (IsPathAllowed(cached.Rules, uri.AbsolutePath), cached.CrawlDelaySeconds);
    }

    private static bool IsPathAllowed(IReadOnlyList<string> disallowedRules, string path)
    {
        foreach (var rule in disallowedRules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;

            if (rule == "/" || path.StartsWith(rule, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ParsedRobots ParseRobotsTxt(string content, string userAgent)
    {
        var disallowed = new List<string>();
        int crawlDelay = 5; // Padrão de cortesia: 5 segundos

        using var reader = new StringReader(content);
        string? line;
        bool isTargetAgent = false;
        bool isWildcardAgent = false;
        var wildcardDisallowed = new List<string>();
        int wildcardDelay = 5;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

            var parts = trimmed.Split(':', 2);
            if (parts.Length < 2) continue;

            var directive = parts[0].Trim().ToLowerInvariant();
            var val = parts[1].Trim();

            if (directive == "user-agent")
            {
                isTargetAgent = val.Equals(userAgent, StringComparison.OrdinalIgnoreCase);
                isWildcardAgent = val == "*";
            }
            else if (directive == "disallow")
            {
                if (isTargetAgent)
                {
                    if (!string.IsNullOrWhiteSpace(val)) disallowed.Add(val);
                }
                else if (isWildcardAgent)
                {
                    if (!string.IsNullOrWhiteSpace(val)) wildcardDisallowed.Add(val);
                }
            }
            else if (directive == "crawl-delay")
            {
                if (int.TryParse(val, out var delay))
                {
                    if (isTargetAgent) crawlDelay = Math.Max(1, Math.Min(delay, 60));
                    else if (isWildcardAgent) wildcardDelay = Math.Max(1, Math.Min(delay, 60));
                }
            }
        }

        // Se encontrou regras específicas para CuiaCrawler, usar essas; senão usar regras para *
        if (disallowed.Count > 0)
        {
            return new ParsedRobots(disallowed, crawlDelay);
        }

        return new ParsedRobots(wildcardDisallowed, wildcardDelay > 0 ? wildcardDelay : 5);
    }

    private sealed record CachedRobotsEntry(
        IReadOnlyList<string> Rules,
        int CrawlDelaySeconds,
        DateTimeOffset ExpiresAt
    );

    private sealed record ParsedRobots(
        IReadOnlyList<string> DisallowedPaths,
        int CrawlDelaySeconds
    );
}
