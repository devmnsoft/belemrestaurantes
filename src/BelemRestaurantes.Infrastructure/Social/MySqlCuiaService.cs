using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Social;

public sealed class MySqlCuiaService(MySqlConnectionFactory factory, ILogger<MySqlCuiaService> logger) : ICuiaService
{
    public async Task RecalculateRankingsAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        const string statsSql = """
            SELECT r.id AS RestaurantId, r.name AS Name, r.city AS City, r.category AS Category,
                   r.created_at AS CreatedAt,
                   COALESCE(AVG(e.stars), 0) AS AvgStars,
                   COALESCE(AVG(e.cost_benefit_stars), 0) AS AvgCostBenefit,
                   COUNT(e.id) AS ApprovedCount,
                   (SELECT COUNT(*) FROM favorites f WHERE f.restaurant_id = r.id) AS FavoriteCount
            FROM restaurants r
            LEFT JOIN user_experiences e ON e.restaurant_id = r.id AND e.status = 'approved' AND e.visibility = 'public'
            WHERE r.status = 'published'
            GROUP BY r.id, r.name, r.city, r.category, r.created_at
            """;

        var stats = (await connection.QueryAsync<RestaurantStatRow>(new CommandDefinition(statsSql, cancellationToken: ct))).AsList();
        if (stats.Count == 0) return;

        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM cuia_rankings", tx, cancellationToken: ct));

        var categories = CuiaRatingRules.CategoryTitles.Keys.ToArray();

        foreach (var categoryKey in categories)
        {
            var badgeTitle = CuiaRatingRules.CategoryTitles[categoryKey];

            var eligible = stats
                .Where(s => CuiaRatingRules.MatchesCuiaCategory(categoryKey, s.City, s.Category, s.AvgCostBenefit, s.FavoriteCount, s.CreatedAt))
                .Select(s =>
                {
                    var score = categoryKey == "favorito_comunidade"
                        ? (s.FavoriteCount * 0.5 + CuiaRatingRules.CalculateBayesianScore(s.AvgStars, s.ApprovedCount) * 0.5)
                        : CuiaRatingRules.CalculateBayesianScore(s.AvgStars, s.ApprovedCount);

                    return new
                    {
                        s.RestaurantId,
                        Score = Math.Round(score, 2),
                        s.ApprovedCount,
                        RawAvg = s.AvgStars
                    };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ApprovedCount)
                .Take(10)
                .ToList();

            var rank = 1;
            foreach (var item in eligible)
            {
                var summary = CuiaRatingRules.GetMethodologySummary(item.RawAvg, item.ApprovedCount, item.Score);

                const string insertSql = """
                    INSERT INTO cuia_rankings (
                        restaurant_id, category_key, rank_position, score,
                        approved_reviews_count, badge_title, methodology_summary, calculated_at
                    ) VALUES (
                        @RestaurantId, @categoryKey, @rank, @Score,
                        @ApprovedCount, @badgeTitle, @summary, UTC_TIMESTAMP(6)
                    )
                    """;

                await connection.ExecuteAsync(new CommandDefinition(insertSql, new
                {
                    item.RestaurantId,
                    categoryKey,
                    rank,
                    item.Score,
                    item.ApprovedCount,
                    badgeTitle,
                    summary
                }, tx, cancellationToken: ct));

                rank++;
            }
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("Rankings do Selo Cuia recalculados com sucesso para {Count} estabelecimentos.", stats.Count);
    }

    public async Task<IReadOnlyList<CuiaBadgeView>> GetBadgesForRestaurantAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT c.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   c.category_key AS CategoryKey, c.badge_title AS BadgeTitle, c.rank_position AS RankPosition,
                   c.score AS Score, c.approved_reviews_count AS ApprovedReviewsCount,
                   c.methodology_summary AS MethodologySummary, c.calculated_at AS CalculatedAt
            FROM cuia_rankings c
            JOIN restaurants r ON r.id = c.restaurant_id
            WHERE c.restaurant_id = @restaurantId
            ORDER BY c.rank_position ASC
            """;

        var rows = await connection.QueryAsync<CuiaBadgeDbRow>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return rows.Select(MapBadge).ToList();
    }

    public async Task<IReadOnlyList<CuiaBadgeView>> GetTopRankingsAsync(string categoryKey, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT c.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   c.category_key AS CategoryKey, c.badge_title AS BadgeTitle, c.rank_position AS RankPosition,
                   c.score AS Score, c.approved_reviews_count AS ApprovedReviewsCount,
                   c.methodology_summary AS MethodologySummary, c.calculated_at AS CalculatedAt
            FROM cuia_rankings c
            JOIN restaurants r ON r.id = c.restaurant_id
            WHERE c.category_key = @categoryKey
            ORDER BY c.rank_position ASC
            LIMIT @limit
            """;

        var rows = await connection.QueryAsync<CuiaBadgeDbRow>(new CommandDefinition(sql, new { categoryKey, limit }, cancellationToken: ct));
        return (IReadOnlyList<CuiaBadgeView>)rows.AsList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<CuiaBadgeView>>> GetAllHighlightsAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT c.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   c.category_key AS CategoryKey, c.badge_title AS BadgeTitle, c.rank_position AS RankPosition,
                   c.score AS Score, c.approved_reviews_count AS ApprovedReviewsCount,
                   c.methodology_summary AS MethodologySummary, c.calculated_at AS CalculatedAt
            FROM cuia_rankings c
            JOIN restaurants r ON r.id = c.restaurant_id
            ORDER BY c.category_key ASC, c.rank_position ASC
            """;

        var rows = await connection.QueryAsync<CuiaBadgeDbRow>(new CommandDefinition(sql, cancellationToken: ct));
        var dict = new Dictionary<string, IReadOnlyList<CuiaBadgeView>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.Select(MapBadge).GroupBy(r => r.CategoryKey))
        {
            dict[group.Key] = group.ToList();
        }

        return dict;
    }

    // Dapper/MySqlConnector exposes BIGINT UNSIGNED as ulong, INT UNSIGNED as uint,
    // DECIMAL as decimal and DATETIME as DateTime. A positional record expecting
    // long/int/double/DateTimeOffset cannot be materialized by constructor matching.
    // Materialize using settable CLR DB types, then convert to the public domain view.
    private sealed class CuiaBadgeDbRow
    {
        public ulong RestaurantId { get; set; }
        public string RestaurantName { get; set; } = "";
        public string RestaurantCity { get; set; } = "";
        public string CategoryKey { get; set; } = "";
        public string BadgeTitle { get; set; } = "";
        public uint RankPosition { get; set; }
        public decimal Score { get; set; }
        public uint ApprovedReviewsCount { get; set; }
        public string MethodologySummary { get; set; } = "";
        public DateTime CalculatedAt { get; set; }
    }

    private static CuiaBadgeView MapBadge(CuiaBadgeDbRow row)
    {
        var utc = DateTime.SpecifyKind(row.CalculatedAt, DateTimeKind.Utc);
        return new CuiaBadgeView(
            checked((long)row.RestaurantId), row.RestaurantName, row.RestaurantCity,
            row.CategoryKey, row.BadgeTitle, checked((int)row.RankPosition),
            (double)row.Score, checked((int)row.ApprovedReviewsCount),
            row.MethodologySummary, new DateTimeOffset(utc));
    }

    private sealed class RestaurantStatRow
    {
        public long RestaurantId { get; set; }
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public string? Category { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public double AvgStars { get; set; }
        public double AvgCostBenefit { get; set; }
        public int ApprovedCount { get; set; }
        public int FavoriteCount { get; set; }
    }
}
