using BelemRestaurantes.Application.Abstractions;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

public sealed class MySqlPlaceDossier(MySqlConnectionFactory factory) : IPlaceDossier
{
    public async Task<PlaceDossier> ForRestaurantAsync(long restaurantId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var fields = (await db.QueryAsync<FieldRow>(new CommandDefinition(
            """
            SELECT field_name FieldName, field_value FieldValue, state State, source_name SourceName, source_url SourceUrl, collected_at CollectedAt, evidence Evidence
            FROM field_provenance WHERE restaurant_id = @restaurantId ORDER BY field_name, id
            """, new { restaurantId }, cancellationToken: ct))).ToArray();
        var dishes = (await db.QueryAsync<PlaceDish>(new CommandDefinition(
            """
            SELECT name Name, attribution Attribution, note Note, source_url SourceUrl, availability Availability
            FROM place_dishes WHERE restaurant_id = @restaurantId ORDER BY name LIMIT 24
            """, new { restaurantId }, cancellationToken: ct))).ToArray();
        var videos = (await db.QueryAsync<VideoRow>(new CommandDefinition(
            """
            SELECT v.id Id, v.title Title, v.author_name Author, v.platform Platform, v.canonical_url Url, v.published_on PublishedOn,
                   v.summary Summary, v.attribution_kind Attribution, v.related_topic Topic
            FROM culinary_videos v
            WHERE EXISTS (
              SELECT 1 FROM para_recipes p JOIN para_recipe_restaurants pr ON pr.recipe_id=p.id
              WHERE pr.restaurant_id=@restaurantId AND p.status='published'
                AND JSON_UNQUOTE(JSON_EXTRACT(p.body,'$.Kind'))='official'
                AND JSON_UNQUOTE(JSON_EXTRACT(p.body,'$.VideoUrl'))=v.canonical_url)
            ORDER BY v.published_on DESC LIMIT 6
            """, new { restaurantId }, cancellationToken: ct))).Select(Map).ToArray();
        return new PlaceDossier(fields.Select(f => new FieldProvenance(f.FieldName, f.FieldValue, f.State, f.SourceName, f.SourceUrl, DateTime.SpecifyKind(f.CollectedAt, DateTimeKind.Utc), f.Evidence)).ToArray(), dishes, videos);
    }

    public async Task<IReadOnlyList<CulinaryVideo>> ListVideosAsync(string? topic, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = await db.QueryAsync<VideoRow>(new CommandDefinition(
            """
            SELECT id Id, title Title, author_name Author, platform Platform, canonical_url Url, published_on PublishedOn,
                   summary Summary, attribution_kind Attribution, related_topic Topic
            FROM culinary_videos
            WHERE @topic IS NULL OR @topic = '' OR related_topic LIKE @like OR title LIKE @like
            ORDER BY published_on DESC, id DESC LIMIT 24
            """, new { topic, like = string.IsNullOrWhiteSpace(topic) ? null : "%" + topic.Trim() + "%" }, cancellationToken: ct));
        return rows.Select(Map).ToArray();
    }

    private static CulinaryVideo Map(VideoRow row) => new(row.Id, row.Title, row.Author, row.Platform, row.Url, row.PublishedOn is null ? null : DateOnly.FromDateTime(row.PublishedOn.Value), row.Summary, row.Attribution, row.Topic);

    private sealed class FieldRow
    {
        public string FieldName { get; set; } = "";
        public string? FieldValue { get; set; }
        public string State { get; set; } = "";
        public string? SourceName { get; set; }
        public string? SourceUrl { get; set; }
        public DateTime CollectedAt { get; set; }
        public string? Evidence { get; set; }
    }

    private sealed class VideoRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTime? PublishedOn { get; set; }
        public string Summary { get; set; } = "";
        public string Attribution { get; set; } = "";
        public string? Topic { get; set; }
    }
}
