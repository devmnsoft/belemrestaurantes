using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

public sealed class MySqlCulinaryResearch(MySqlConnectionFactory factory, ISafeHttpClient http,
    IRobotsTxtService robots, ICrawlerParser parser, ILogger<MySqlCulinaryResearch> logger) : ICulinaryResearch
{
    private sealed class Row { public long Id { get; set; } public string Body { get; set; } = ""; }
    private sealed class Job { public long Id { get; set; } public string Url { get; set; } = ""; public string Topic { get; set; } = ""; public int Attempts { get; set; } }
    public async Task<IReadOnlyList<CulinaryReference>> FindAsync(string query, CancellationToken ct)
    {
        var terms = CulinaryQuery.Core(query).Take(6).ToArray();
        var browse=BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(query) is "receitas" or "receitas paraenses" or "culinaria paraense";
        if (terms.Length == 0&&!browse) return [];
        await using var db = await factory.OpenAsync(ct);
        var p = new DynamicParameters();
        for (var i = 0; i < terms.Length; i++) p.Add("t" + i, "%" + MySqlRestaurantReadRepository.EscapeLike(terms[i]) + "%");
        var where = browse?"kind='recipe_reference'":string.Join(" AND ", terms.Select((_, i) => "search_text LIKE @t" + i));
        if(browse&&query.Contains("paraense",StringComparison.OrdinalIgnoreCase))where+=" AND (topic LIKE '%tacacá%' OR canonical_url LIKE '%/regionais/tv-liberal/%' OR search_text LIKE '%paraense%')";
        var rows = await db.QueryAsync<Row>(new CommandDefinition("SELECT id,body FROM culinary_references WHERE state='published' AND " + where + " ORDER BY observed_at DESC LIMIT 24", p, cancellationToken: ct));
        return rows.Select(r => JsonSerializer.Deserialize<CulinaryReference>(r.Body)! with { Id = r.Id }).ToArray();
    }
    public async Task<CulinaryReference?> GetAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("SELECT id,body FROM culinary_references WHERE id=@id AND state='published'", new { id }, cancellationToken: ct));
        return row is null ? null : JsonSerializer.Deserialize<CulinaryReference>(row.Body)! with { Id = row.Id };
    }
    public async Task EnqueueAsync(IEnumerable<string> urls, string topic, CancellationToken ct)
    {
        topic = ExternalContentRules.Clean(topic, 180);
        if (topic.Length < 3 || SearchTopics.IsPrivate(topic)) return;
        await using var db = await factory.OpenAsync(ct);
        foreach (var candidate in urls.Take(12))
        {
            var url = ExternalContentRules.CanonicalUrl(candidate);
            if (url is null || ResearchUrlIsSocial(url) || new Uri(url).Host.EndsWith(".wikipedia.org",StringComparison.OrdinalIgnoreCase)) continue;
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO culinary_tasks(url_hash,url,topic,next_run_at,updated_at) VALUES(@hash,@url,@topic,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE state=IF(state IN ('completed','rejected') AND next_run_at<=UTC_TIMESTAMP(6),'pending',state)
                """, new { hash = Hash(url), url, topic }, cancellationToken: ct));
        }
    }
    public async Task<CulinaryCollection> CollectAsync(CancellationToken ct)
    {
        var published = 0; var discovered = 0; var deferred = 0;
        for (var n = 0; n < 3; n++)
        {
            Job? job;
            await using (var db = await factory.OpenAsync(ct))
            await using (var tx = await db.BeginTransactionAsync(ct))
            {
                job = await db.QuerySingleOrDefaultAsync<Job>(new CommandDefinition("""
                    SELECT id,url,topic,attempts FROM culinary_tasks WHERE
                    (state='pending' AND next_run_at<=UTC_TIMESTAMP(6)) OR (state='running' AND lease_until<UTC_TIMESTAMP(6))
                    ORDER BY next_run_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                    """, transaction: tx, cancellationToken: ct));
                if (job is not null) await db.ExecuteAsync(new CommandDefinition("UPDATE culinary_tasks SET state='running',attempts=attempts+1,lease_until=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 2 MINUTE),updated_at=UTC_TIMESTAMP(6) WHERE id=@Id", job, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
            }
            if (job is null) break;
            discovered++;
            var status = "unavailable"; var success = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(22));
                var access = await robots.CheckAccessAsync(job.Url, "CuiaCrawler", deadline.Token);
                if (!access.IsAllowed || access.CrawlDelaySeconds > 10) status = "restricted";
                else
                {
                    if (access.CrawlDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(access.CrawlDelaySeconds), deadline.Token);
                    var page = await http.DownloadAsync(job.Url, timeoutSeconds: 10, ct: deadline.Token);
                    if (page.Success && page.ContentText is { Length: > 0 } html && page.ContentType?.Contains("html") == true)
                    {
                        var records = CulinaryStructuredParser.Parse(html, job.Url, job.Topic, DateTime.UtcNow).ToList();
                        records.AddRange(RemansoMenuSource.Parse(html,job.Url,DateTime.UtcNow));
                        foreach (var place in parser.ExtractRestaurants(html, job.Url, "Belém").Where(p => p.ExplicitCity && p.City == "Belém" && !p.HasConflict && p.ConfidenceScore >= .95))
                            foreach (var dish in place.MenuItems.Where(d => CulinaryQuery.Relevant(job.Topic, d, "")).Take(8))
                                records.Add(new(0, "dish_evidence", dish, job.Url, new Uri(job.Url).Host,
                                    "Prato mencionado nos dados estruturados do estabelecimento. Confirme disponibilidade no canal original.", [], null, place.Name, place.City, place.Address, [], DateTime.UtcNow, null,
                                    "MenuItem associado ao estabelecimento na página. Não comprova receita oficial nem disponibilidade de hoje.", null));
                        foreach (var record in records) { await PublishAsync(record, job.Topic, deadline.Token); published++; }
                        status = records.Count > 0 ? "published" : "no_structured_evidence";
                        success = true;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException)
            { logger.LogWarning("Coleta culinária {TaskId}: {Failure}.", job.Id, ex.GetType().Name); status = ex is OperationCanceledException ? "timeout" : "invalid_source"; }
            await using var finish = await factory.OpenAsync(ct);
            var rejected = !success && (status == "restricted" || job.Attempts >= 3);
            await finish.ExecuteAsync(new CommandDefinition("""
                UPDATE culinary_tasks SET state=@state,last_status=@status,lease_until=NULL,
                next_run_at=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL @minutes MINUTE),updated_at=UTC_TIMESTAMP(6) WHERE id=@Id
                """, new { job.Id, status, state = success ? "completed" : rejected ? "rejected" : "pending", minutes = success || rejected ? 10080 : (int)Math.Pow(2, Math.Min(job.Attempts + 1, 6)) }, cancellationToken: ct));
            if (!success) deferred++;
        }
        return new(discovered, published, deferred);
    }
    private async Task PublishAsync(CulinaryReference record, string topic, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var hash = Hash(record.Url + "|" + record.Kind + "|" + record.Title);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO culinary_references(url_hash,canonical_url,kind,title,topic,search_text,body,observed_at)
            VALUES(@hash,@Url,@Kind,@Title,@topic,@search,@body,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE body=@body,search_text=@search,observed_at=UTC_TIMESTAMP(6)
            """, new { hash, record.Url, record.Kind, record.Title, topic, search = string.Join(' ', new[] { record.Title, record.PlaceName, record.City, topic }.Concat(record.Ingredients)), body = JsonSerializer.Serialize(record) }, tx, cancellationToken: ct));
        if (record.VideoUrl is { } video)
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO culinary_videos(title,author_name,platform,canonical_url,published_on,related_topic,summary,attribution_kind,url_hash,observed_at)
                VALUES(@Title,@Author,'YouTube',@video,@date,@topic,@Summary,'author_published',@hash,UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE observed_at=UTC_TIMESTAMP(6),published_on=IF(attribution_kind='author_published',NULL,published_on)
                """, new { record.Title, record.Author, video, date = (DateTime?)null, topic, record.Summary, hash = Hash(video) }, tx, cancellationToken: ct));
        if(record.Kind=="dish_evidence" && record.PlaceName is {} placeName && record.City=="Belém")
        {
            var names=new[]{placeName,placeName.Replace("Restaurante ","",StringComparison.OrdinalIgnoreCase)};
            var matches=(await db.QueryAsync<long>(new CommandDefinition("SELECT id FROM restaurants WHERE status='published' AND city='Belém' AND name IN @names LIMIT 2 FOR UPDATE",new{names},tx,cancellationToken:ct))).ToArray();
            if(matches.Length==1)
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO place_dishes(restaurant_id,name,attribution,note,source_url,availability,observed_at)
                    SELECT @id,@Title,'restaurant_dish',@note,@Url,'confirm_with_place',UTC_TIMESTAMP(6)
                    WHERE NOT EXISTS(SELECT 1 FROM place_dishes WHERE restaurant_id=@id AND name=@Title AND source_url=@Url)
                    """,new{id=matches[0],record.Title,record.Url,note=record.Evidence},tx,cancellationToken:ct));
        }
        await tx.CommitAsync(ct);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool ResearchUrlIsSocial(string url) => BelemRestaurantes.Infrastructure.Ai.ResearchEvidence.IsSocial(new Uri(url).Host);
}
