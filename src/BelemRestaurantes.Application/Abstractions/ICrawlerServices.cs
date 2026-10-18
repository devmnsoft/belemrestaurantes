using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record SafeDownloadResult(
    bool Success,
    int StatusCode,
    string? ContentText,
    string? ContentType,
    string? ETag,
    string? LastModified,
    string? ErrorMessage,
    bool NotModified = false
);

public sealed record CrawlerSummaryStats(
    int TotalSources,
    int ActiveSources,
    int BlockedSources,
    int TotalTasksPending,
    int TotalRunsToday,
    int TotalDiscoveredToday,
    int TotalIngestedToday,
    int TotalErrorsToday
)
{
    // COUNT retorna Int64 e SUM retorna Decimal no MySQL. O construtor
    // vazio permite ao Dapper converter cada coluna para a propriedade Int32.
    public CrawlerSummaryStats() : this(0, 0, 0, 0, 0, 0, 0, 0) { }
}

public interface ISafeHttpClient
{
    /// <summary>
    /// Efetua requisição HTTP com validação rigorosa contra SSRF e DNS Rebinding,
    /// limitando o tamanho da resposta (max 2MB), timeout rígido e validação de Content-Type.
    /// </summary>
    Task<SafeDownloadResult> DownloadAsync(
        string url,
        string? etag = null,
        string? lastModified = null,
        int timeoutSeconds = 15,
        CancellationToken ct = default);
}

public interface IRobotsTxtService
{
    /// <summary>
    /// Consulta e analisa o robots.txt do domínio alvo, respeitando diretivas de User-Agent e Crawl-delay.
    /// </summary>
    Task<(bool IsAllowed, int CrawlDelaySeconds)> CheckAccessAsync(string url, string userAgent = "CuiaCrawler", CancellationToken ct = default);
}

public interface ICrawlerParser
{
    /// <summary>
    /// Extrai URLs válidas de um documento Sitemap XML (suporta sitemapindex e urlset).
    /// </summary>
    IReadOnlyList<string> ParseSitemap(string xmlContent, string baseUrl);

    /// <summary>
    /// Extrai links e títulos de um feed RSS ou Atom gastronômico.
    /// </summary>
    IReadOnlyList<(string Url, string Title, string? Description)> ParseRssFeed(string xmlContent, string baseUrl);

    /// <summary>
    /// Extrai dados estruturados Schema.org (Restaurant, FoodEstablishment) e heurística de conteúdo em páginas HTML.
    /// </summary>
    IReadOnlyList<ExtractedRestaurantData> ExtractRestaurants(string htmlContent, string sourceUrl, string defaultCity);
}

public interface ICrawlerSourceRepository
{
    Task<IReadOnlyList<CrawlerSource>> ListSourcesAsync(string? status, CancellationToken ct);
    Task<CrawlerSource?> GetByIdAsync(long id, CancellationToken ct);
    Task<CrawlerSource?> GetByBaseUrlAsync(string baseUrl, CancellationToken ct);
    Task<long> CreateSourceAsync(CrawlerSource source, CancellationToken ct);
    Task<bool> UpdateStatusAsync(long id, string status, CancellationToken ct);
    Task<bool> UpdateRobotsStatusAsync(long id, string robotsStatus, int crawlDelay, CancellationToken ct);
    Task RecordCrawlResultAsync(long id, bool success, string? error, string? etag, string? lastModified, CancellationToken ct);
}

public interface ICrawlerTaskQueue
{
    Task<bool> EnqueueTaskAsync(CrawlerTask task, CancellationToken ct);
    Task<int> EnqueueBatchTasksAsync(IEnumerable<CrawlerTask> tasks, CancellationToken ct);
    Task<IReadOnlyList<CrawlerTask>> ClaimDueTasksAsync(int batchSize, string instanceId, TimeSpan lockDuration, CancellationToken ct);
    Task CompleteTaskAsync(long taskId, CancellationToken ct);
    Task FailTaskAsync(long taskId, string error, TimeSpan backoff, int maxAttempts, CancellationToken ct);
    Task<int> ReleaseStaleLocksAsync(DateTimeOffset now, CancellationToken ct);
    Task<int> CountPendingAsync(CancellationToken ct);
}

public interface ICrawlerTelemetryRepository
{
    Task RecordRunAsync(CrawlerRunLog run, CancellationToken ct);
    Task<IReadOnlyList<CrawlerRunLog>> ListRecentRunsAsync(int limit, CancellationToken ct);
    Task<CrawlerSummaryStats> GetSummaryStatsAsync(CancellationToken ct);
}

public interface ICrawlerOrchestrator
{
    /// <summary>
    /// Executa uma rodada autônoma de rastreamento:
    /// 1. Sincroniza sitemaps de fontes ativas
    /// 2. Executa lote de tarefas da fila persistente
    /// 3. Respeita robots.txt e limites SSRF
    /// 4. Extrai Schema.org e enriquece via Cuia IA
    /// 5. Deduplica e envia novos estabelecimentos para moderação
    /// 6. Registra telemetria e atualiza custos
    /// </summary>
    Task<int> RunCrawlerCycleAsync(int maxTasks, CancellationToken ct);
}
