using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
namespace BelemRestaurantes.Infrastructure.Ai;
public sealed class MySqlSearchArchive(MySqlConnectionFactory factory):ISearchArchive
{
    public async Task<WebSearchResult?> GetAsync(string key,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var json=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT result_json FROM search_result_cache WHERE query_hash=@key AND expires_at>UTC_TIMESTAMP(6)",new{key},cancellationToken:ct));
        if(json is null)return null;
        try{return JsonSerializer.Deserialize<WebSearchResult>(json);}catch(JsonException){return null;}
    }
    public async Task SaveAsync(string key,WebSearchResult result,CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var seconds=result.Sources.Count>0?1200:result.Status=="empty"?120:20;
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO search_result_cache(query_hash,result_json,expires_at,updated_at) VALUES(@key,@json,TIMESTAMPADD(SECOND,@seconds,UTC_TIMESTAMP(6)),UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE result_json=VALUES(result_json),expires_at=VALUES(expires_at),updated_at=VALUES(updated_at)",new{key,json=JsonSerializer.Serialize(result),seconds},cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM search_result_cache WHERE expires_at<UTC_TIMESTAMP(6) LIMIT 100",cancellationToken:ct));
    }
    public async Task RecordTopicsAsync(string query,CancellationToken ct)
    {
        var topics=SearchTopics.Extract(query);if(topics.Count==0)return;
        await using var db=await factory.OpenAsync(ct);
        foreach(var topic in topics)await db.ExecuteAsync(new CommandDefinition("INSERT INTO search_topic_daily(topic,search_day,hits) VALUES(@topic,UTC_DATE(),1) ON DUPLICATE KEY UPDATE hits=hits+1",new{topic},cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM search_topic_daily WHERE search_day<DATE_SUB(UTC_DATE(),INTERVAL 30 DAY)",cancellationToken:ct));
    }
    public async Task<IReadOnlyList<SearchTopic>> TopicsAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        return (await db.QueryAsync<SearchTopic>(new CommandDefinition("SELECT topic Label,CAST(SUM(hits) AS SIGNED) Count FROM search_topic_daily WHERE search_day>=DATE_SUB(UTC_DATE(),INTERVAL 30 DAY) GROUP BY topic ORDER BY Count DESC,topic LIMIT 24",cancellationToken:ct))).ToArray();
    }
}
