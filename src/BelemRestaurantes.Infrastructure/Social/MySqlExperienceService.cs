using System.Security.Cryptography;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace BelemRestaurantes.Infrastructure.Social;

public sealed class MySqlExperienceService(MySqlConnectionFactory factory, ILogger<MySqlExperienceService> logger) : IExperienceService
{
    public async Task<long> CreateAsync(CreateExperienceInput input, CancellationToken ct)
    {
        ValidateStars(input.Stars, input.ServiceStars, input.FlavorStars, input.AmbianceStars, input.CostBenefitStars);

        var analysis = ContentModerationRules.Analyze(input.ReviewText);
        var initialStatus = analysis.ContainsOffensiveLanguage || analysis.ContainsPii ? "rejected" : "pending";
        var rejectionReason = analysis.ViolationReason;

        var unlistedToken = input.Visibility.Equals("unlisted", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            : null;

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            INSERT INTO user_experiences (
                restaurant_id, user_id, author_name, stars, service_stars, flavor_stars,
                ambiance_stars, cost_benefit_stars, review_text, dishes_consumed, price_paid,
                visibility, unlisted_token, status, rejection_reason, created_at, updated_at
            ) VALUES (
                @RestaurantId, @UserId, @AuthorName, @Stars, @ServiceStars, @FlavorStars,
                @AmbianceStars, @CostBenefitStars, @ReviewText, @DishesConsumed, @PricePaid,
                @Visibility, @UnlistedToken, @Status, @RejectionReason, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            );
            SELECT LAST_INSERT_ID();
            """;

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            input.RestaurantId,
            input.UserId,
            AuthorName = input.AuthorName.Trim(),
            input.Stars,
            input.ServiceStars,
            input.FlavorStars,
            input.AmbianceStars,
            input.CostBenefitStars,
            ReviewText = analysis.CleanedText,
            DishesConsumed = input.DishesConsumed?.Trim(),
            input.PricePaid,
            Visibility = input.Visibility.ToLowerInvariant(),
            UnlistedToken = unlistedToken,
            Status = initialStatus,
            RejectionReason = rejectionReason
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, occurred_at)
            VALUES (@UserId, 'experience.create', 'user_experience', @id, UTC_TIMESTAMP(6))
            """, new { input.UserId, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        logger.LogInformation("Experiência {Id} registrada para o restaurante {RestaurantId}.", id, input.RestaurantId);
        return id;
    }

    public async Task<bool> UpdateAsync(UpdateExperienceInput input, CancellationToken ct)
    {
        ValidateStars(input.Stars, input.ServiceStars, input.FlavorStars, input.AmbianceStars, input.CostBenefitStars);

        var analysis = ContentModerationRules.Analyze(input.ReviewText);
        var unlistedToken = input.Visibility.Equals("unlisted", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            : null;

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            UPDATE user_experiences
            SET stars = @Stars,
                service_stars = @ServiceStars,
                flavor_stars = @FlavorStars,
                ambiance_stars = @AmbianceStars,
                cost_benefit_stars = @CostBenefitStars,
                review_text = @ReviewText,
                dishes_consumed = @DishesConsumed,
                price_paid = @PricePaid,
                visibility = @Visibility,
                unlisted_token = COALESCE(unlisted_token, @UnlistedToken),
                status = 'pending',
                rejection_reason = NULL,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id AND user_id = @UserId
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            input.Id,
            input.UserId,
            input.Stars,
            input.ServiceStars,
            input.FlavorStars,
            input.AmbianceStars,
            input.CostBenefitStars,
            ReviewText = analysis.CleanedText,
            DishesConsumed = input.DishesConsumed?.Trim(),
            input.PricePaid,
            Visibility = input.Visibility.ToLowerInvariant(),
            UnlistedToken = unlistedToken
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, occurred_at)
            VALUES (@UserId, 'experience.update', 'user_experience', @Id, UTC_TIMESTAMP(6))
            """, new { input.UserId, input.Id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(long id, string userId, bool isAdmin, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var query = isAdmin
            ? "DELETE FROM user_experiences WHERE id = @id"
            : "DELETE FROM user_experiences WHERE id = @id AND user_id = @userId";

        var affected = await connection.ExecuteAsync(new CommandDefinition(query, new { id, userId }, tx, cancellationToken: ct));
        if (affected > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, occurred_at)
                VALUES (@userId, 'experience.delete', 'user_experience', @id, UTC_TIMESTAMP(6))
                """, new { userId, id }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<ExperienceView?> GetByIdAsync(long id, string? viewerUserId, string? unlistedToken, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT e.id, e.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   e.user_id AS UserId, e.author_name AS AuthorName, e.stars AS Stars,
                   e.service_stars AS ServiceStars, e.flavor_stars AS FlavorStars, e.ambiance_stars AS AmbianceStars,
                   e.cost_benefit_stars AS CostBenefitStars, e.review_text AS ReviewText,
                   e.dishes_consumed AS DishesConsumed, e.price_paid AS PricePaid,
                   e.visibility AS Visibility, e.unlisted_token AS UnlistedToken,
                   e.status AS Status, e.rejection_reason AS RejectionReason, e.appeal_status AS AppealStatus, e.appeal_text AS AppealText,
                   e.likes_count AS LikesCount, e.comments_count AS CommentsCount,
                   e.created_at AS CreatedAt, e.published_at AS PublishedAt
            FROM user_experiences e
            JOIN restaurants r ON r.id = e.restaurant_id
            WHERE e.id = @id
            LIMIT 1
            """;

        var row = await connection.QuerySingleOrDefaultAsync<ExperienceRow>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        if (row is null) return null;

        var isOwner = !string.IsNullOrEmpty(viewerUserId) && row.UserId == viewerUserId;
        var isPublicApproved = row.Status == "approved" && row.Visibility == "public";
        var isUnlistedAllowed = row.Visibility == "unlisted" && !string.IsNullOrEmpty(unlistedToken) && string.Equals(row.UnlistedToken, unlistedToken, StringComparison.Ordinal);

        if (!isPublicApproved && !isOwner && !isUnlistedAllowed)
        {
            return null; // IDOR protegido
        }

        var photos = await LoadPhotosAsync(connection, [row.Id], ct);
        var hasLiked = !string.IsNullOrEmpty(viewerUserId) && await HasUserLikedAsync(row.Id, viewerUserId, ct);

        return MapView(row, photos.GetValueOrDefault(row.Id, []), hasLiked);
    }

    public async Task<IReadOnlyList<ExperienceView>> ListPublicByRestaurantAsync(long restaurantId, int page, int pageSize, string? viewerUserId, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT e.id, e.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   e.user_id AS UserId, e.author_name AS AuthorName, e.stars AS Stars,
                   e.service_stars AS ServiceStars, e.flavor_stars AS FlavorStars, e.ambiance_stars AS AmbianceStars,
                   e.cost_benefit_stars AS CostBenefitStars, e.review_text AS ReviewText,
                   e.dishes_consumed AS DishesConsumed, e.price_paid AS PricePaid,
                   e.visibility AS Visibility, e.unlisted_token AS UnlistedToken,
                   e.status AS Status, e.rejection_reason AS RejectionReason, e.appeal_status AS AppealStatus, e.appeal_text AS AppealText,
                   e.likes_count AS LikesCount, e.comments_count AS CommentsCount,
                   e.created_at AS CreatedAt, e.published_at AS PublishedAt
            FROM user_experiences e
            JOIN restaurants r ON r.id = e.restaurant_id
            WHERE e.restaurant_id = @restaurantId AND e.status = 'approved' AND e.visibility = 'public'
            ORDER BY e.published_at DESC, e.created_at DESC
            LIMIT @limit OFFSET @offset
            """;

        var rows = (await connection.QueryAsync<ExperienceRow>(new CommandDefinition(sql, new { restaurantId, limit = pageSize, offset }, cancellationToken: ct))).AsList();
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToArray();
        var photos = await LoadPhotosAsync(connection, ids, ct);
        var likedIds = !string.IsNullOrEmpty(viewerUserId) ? await LoadLikedIdsAsync(connection, viewerUserId, ids, ct) : new HashSet<long>();

        return rows.Select(r => MapView(r, photos.GetValueOrDefault(r.Id, []), likedIds.Contains(r.Id))).AsList();
    }

    public async Task<IReadOnlyList<ExperienceView>> ListPublicFeedAsync(int page, int pageSize, string? sort, string? viewerUserId, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var orderBy = sort?.ToLowerInvariant() == "top"
            ? "e.likes_count DESC, e.stars DESC, e.published_at DESC"
            : "e.published_at DESC, e.created_at DESC";

        await using var connection = await factory.OpenAsync(ct);
        var sql = $"""
            SELECT e.id, e.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   e.user_id AS UserId, e.author_name AS AuthorName, e.stars AS Stars,
                   e.service_stars AS ServiceStars, e.flavor_stars AS FlavorStars, e.ambiance_stars AS AmbianceStars,
                   e.cost_benefit_stars AS CostBenefitStars, e.review_text AS ReviewText,
                   e.dishes_consumed AS DishesConsumed, e.price_paid AS PricePaid,
                   e.visibility AS Visibility, e.unlisted_token AS UnlistedToken,
                   e.status AS Status, e.rejection_reason AS RejectionReason, e.appeal_status AS AppealStatus, e.appeal_text AS AppealText,
                   e.likes_count AS LikesCount, e.comments_count AS CommentsCount,
                   e.created_at AS CreatedAt, e.published_at AS PublishedAt
            FROM user_experiences e
            JOIN restaurants r ON r.id = e.restaurant_id
            WHERE e.status = 'approved' AND e.visibility = 'public'
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset
            """;

        var rows = (await connection.QueryAsync<ExperienceRow>(new CommandDefinition(sql, new { limit = pageSize, offset }, cancellationToken: ct))).AsList();
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToArray();
        var photos = await LoadPhotosAsync(connection, ids, ct);
        var likedIds = !string.IsNullOrEmpty(viewerUserId) ? await LoadLikedIdsAsync(connection, viewerUserId, ids, ct) : new HashSet<long>();

        return rows.Select(r => MapView(r, photos.GetValueOrDefault(r.Id, []), likedIds.Contains(r.Id))).AsList();
    }

    public async Task<IReadOnlyList<ExperienceView>> ListUserExperiencesAsync(string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT e.id, e.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   e.user_id AS UserId, e.author_name AS AuthorName, e.stars AS Stars,
                   e.service_stars AS ServiceStars, e.flavor_stars AS FlavorStars, e.ambiance_stars AS AmbianceStars,
                   e.cost_benefit_stars AS CostBenefitStars, e.review_text AS ReviewText,
                   e.dishes_consumed AS DishesConsumed, e.price_paid AS PricePaid,
                   e.visibility AS Visibility, e.unlisted_token AS UnlistedToken,
                   e.status AS Status, e.rejection_reason AS RejectionReason, e.appeal_status AS AppealStatus, e.appeal_text AS AppealText,
                   e.likes_count AS LikesCount, e.comments_count AS CommentsCount,
                   e.created_at AS CreatedAt, e.published_at AS PublishedAt
            FROM user_experiences e
            JOIN restaurants r ON r.id = e.restaurant_id
            WHERE e.user_id = @userId
            ORDER BY e.created_at DESC
            LIMIT 200
            """;

        var rows = (await connection.QueryAsync<ExperienceRow>(new CommandDefinition(sql, new { userId }, cancellationToken: ct))).AsList();
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToArray();
        var photos = await LoadPhotosAsync(connection, ids, ct);

        return rows.Select(r => MapView(r, photos.GetValueOrDefault(r.Id, []), false)).AsList();
    }

    public async Task<IReadOnlyList<ExperienceView>> ListPendingModerationAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT e.id, e.restaurant_id AS RestaurantId, r.name AS RestaurantName, r.city AS RestaurantCity,
                   e.user_id AS UserId, e.author_name AS AuthorName, e.stars AS Stars,
                   e.service_stars AS ServiceStars, e.flavor_stars AS FlavorStars, e.ambiance_stars AS AmbianceStars,
                   e.cost_benefit_stars AS CostBenefitStars, e.review_text AS ReviewText,
                   e.dishes_consumed AS DishesConsumed, e.price_paid AS PricePaid,
                   e.visibility AS Visibility, e.unlisted_token AS UnlistedToken,
                   e.status AS Status, e.rejection_reason AS RejectionReason, e.appeal_status AS AppealStatus, e.appeal_text AS AppealText,
                   e.likes_count AS LikesCount, e.comments_count AS CommentsCount,
                   e.created_at AS CreatedAt, e.published_at AS PublishedAt
            FROM user_experiences e
            JOIN restaurants r ON r.id = e.restaurant_id
            WHERE e.status = 'pending' OR e.appeal_status = 'pending'
            ORDER BY e.created_at ASC
            LIMIT @limit OFFSET @offset
            """;

        var rows = (await connection.QueryAsync<ExperienceRow>(new CommandDefinition(sql, new { limit = pageSize, offset }, cancellationToken: ct))).AsList();
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToArray();
        var photos = await LoadPhotosAsync(connection, ids, ct);

        return rows.Select(r => MapView(r, photos.GetValueOrDefault(r.Id, []), false)).AsList();
    }

    public async Task<bool> ModerateAsync(long id, string newStatus, string? reason, string moderatorActor, CancellationToken ct)
    {
        if (newStatus is not ("approved" or "rejected")) return false;

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            UPDATE user_experiences
            SET status = @newStatus,
                rejection_reason = @reason,
                published_at = CASE WHEN @newStatus = 'approved' THEN UTC_TIMESTAMP(6) ELSE published_at END,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { id, newStatus, reason }, tx, cancellationToken: ct));

        if (affected > 0 && newStatus == "approved")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE experience_photos
                SET moderation_status = 'approved', moderated_by = @moderatorActor, moderated_at = UTC_TIMESTAMP(6)
                WHERE experience_id = @id AND moderation_status = 'pending'
                """, new { id, moderatorActor }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit_events (actor_subject, action, entity_type, entity_id, metadata, occurred_at)
            VALUES (@moderatorActor, @action, 'user_experience', @id, @meta, UTC_TIMESTAMP(6))
            """, new
        {
            moderatorActor,
            action = "experience.moderate:" + newStatus,
            id,
            meta = System.Text.Json.JsonSerializer.Serialize(new { status = newStatus, reason })
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<bool> SubmitAppealAsync(long id, string userId, string appealText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appealText) || appealText.Length > 1000) return false;

        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            UPDATE user_experiences
            SET appeal_text = @appealText, appeal_status = 'pending', updated_at = UTC_TIMESTAMP(6)
            WHERE id = @id AND user_id = @userId AND status = 'rejected'
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { id, userId, appealText = appealText.Trim() }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> ReviewAppealAsync(long id, bool accept, string moderatorActor, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var appealStatus = accept ? "accepted" : "denied";
        var experienceStatus = accept ? "approved" : "rejected";

        const string sql = """
            UPDATE user_experiences
            SET appeal_status = @appealStatus,
                appeal_reviewed_by = @moderatorActor,
                appeal_reviewed_at = UTC_TIMESTAMP(6),
                status = @experienceStatus,
                published_at = CASE WHEN @accept THEN UTC_TIMESTAMP(6) ELSE published_at END,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @id
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            id, appealStatus, moderatorActor, experienceStatus, accept
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<bool> ToggleLikeAsync(long experienceId, string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM experience_likes WHERE user_id = @userId AND experience_id = @experienceId",
            new { userId, experienceId }, tx, cancellationToken: ct));

        if (exists > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM experience_likes WHERE user_id = @userId AND experience_id = @experienceId",
                new { userId, experienceId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_experiences SET likes_count = GREATEST(0, CAST(likes_count AS SIGNED) - 1) WHERE id = @experienceId",
                new { experienceId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return false;
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO experience_likes (user_id, experience_id) VALUES (@userId, @experienceId)",
                new { userId, experienceId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_experiences SET likes_count = likes_count + 1 WHERE id = @experienceId",
                new { experienceId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return true;
        }
    }

    public async Task<bool> HasUserLikedAsync(long experienceId, string userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM experience_likes WHERE user_id = @userId AND experience_id = @experienceId",
            new { userId, experienceId }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<long> AddCommentAsync(long experienceId, string userId, string authorName, string commentText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(commentText) || commentText.Length > 1000)
            throw new ArgumentException("Comentário inválido.");

        var analysis = ContentModerationRules.Analyze(commentText);
        if (analysis.ContainsOffensiveLanguage || analysis.ContainsPii)
            throw new ArgumentException("Comentário rejeitado por moderação.");

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            INSERT INTO experience_comments (experience_id, user_id, author_name, comment_text, created_at)
            VALUES (@experienceId, @userId, @authorName, @commentText, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            experienceId, userId, authorName = authorName.Trim(), commentText = analysis.CleanedText
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_experiences SET comments_count = comments_count + 1 WHERE id = @experienceId",
            new { experienceId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<CommentView>> ListCommentsAsync(long experienceId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id, experience_id AS ExperienceId, user_id AS UserId, author_name AS AuthorName,
                   comment_text AS CommentText, created_at AS CreatedAt
            FROM experience_comments
            WHERE experience_id = @experienceId AND is_deleted = FALSE
            ORDER BY created_at ASC
            LIMIT 100
            """;

        var rows = await connection.QueryAsync<CommentView>(new CommandDefinition(sql, new { experienceId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> DeleteCommentAsync(long commentId, string userId, bool isAdmin, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var query = isAdmin
            ? "UPDATE experience_comments SET is_deleted = TRUE WHERE id = @commentId AND is_deleted = FALSE"
            : "UPDATE experience_comments SET is_deleted = TRUE WHERE id = @commentId AND user_id = @userId AND is_deleted = FALSE";

        var affected = await connection.ExecuteAsync(new CommandDefinition(query, new { commentId, userId }, tx, cancellationToken: ct));
        if (affected > 0)
        {
            var expId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT experience_id FROM experience_comments WHERE id = @commentId", new { commentId }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_experiences SET comments_count = GREATEST(0, CAST(comments_count AS SIGNED) - 1) WHERE id = @expId",
                new { expId }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<long> ReportAsync(string reporterUserId, string targetType, long targetId, string reason, string? notes, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            INSERT INTO community_reports (reporter_user_id, target_type, target_id, reason, notes, created_at)
            VALUES (@reporterUserId, @targetType, @targetId, @reason, @notes, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            reporterUserId, targetType, targetId, reason, notes = notes?.Trim()
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CommunityReport>> ListPendingReportsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var connection = await factory.OpenAsync(ct);
        const string sql = """
            SELECT id, reporter_user_id AS ReporterUserId, target_type AS TargetType, target_id AS TargetId,
                   reason, notes, status, reviewed_by AS ReviewedBy, reviewed_at AS ReviewedAt, created_at AS CreatedAt
            FROM community_reports
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @limit OFFSET @offset
            """;

        var rows = await connection.QueryAsync<CommunityReport>(new CommandDefinition(sql, new { limit = pageSize, offset }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> ReviewReportAsync(long reportId, bool uphold, string moderatorActor, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = uphold ? "reviewed_upheld" : "reviewed_dismissed";
        const string sql = """
            UPDATE community_reports
            SET status = @status, reviewed_by = @moderatorActor, reviewed_at = UTC_TIMESTAMP(6)
            WHERE id = @reportId
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { reportId, status, moderatorActor }, tx, cancellationToken: ct));

        if (affected > 0 && uphold)
        {
            var report = await connection.QuerySingleOrDefaultAsync<CommunityReport>(new CommandDefinition(
                "SELECT target_type AS TargetType, target_id AS TargetId FROM community_reports WHERE id = @reportId",
                new { reportId }, tx, cancellationToken: ct));

            if (report?.TargetType == "experience")
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE user_experiences SET status = 'rejected', rejection_reason = 'Removido após denúncia da comunidade' WHERE id = @targetId",
                    new { targetId = report.TargetId }, tx, cancellationToken: ct));
            }
            else if (report?.TargetType == "comment")
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE experience_comments SET is_deleted = TRUE WHERE id = @targetId",
                    new { targetId = report.TargetId }, tx, cancellationToken: ct));
            }
        }

        await tx.CommitAsync(ct);
        return affected > 0;
    }

    private static void ValidateStars(int stars, int s, int f, int a, int c)
    {
        if (stars is < 1 or > 5 || s is < 1 or > 5 || f is < 1 or > 5 || a is < 1 or > 5 || c is < 1 or > 5)
            throw new ArgumentException("Notas devem estar compreendidas entre 1 e 5 estrelas.");
    }

    private static async Task<Dictionary<long, List<ExperiencePhotoView>>> LoadPhotosAsync(MySqlConnection connection, IReadOnlyList<long> expIds, CancellationToken ct)
    {
        const string sql = """
            SELECT id, experience_id AS ExperienceId, restaurant_id AS RestaurantId, file_name AS FileName,
                   thumbnail_name AS ThumbnailName, caption AS Caption, photo_type AS PhotoType,
                   moderation_status AS ModerationStatus, width AS Width, height AS Height
            FROM experience_photos
            WHERE experience_id IN @expIds
            ORDER BY created_at ASC
            """;

        var rows = await connection.QueryAsync<ExperiencePhotoView>(new CommandDefinition(sql, new { expIds }, cancellationToken: ct));
        var dict = new Dictionary<long, List<ExperiencePhotoView>>();
        foreach (var p in rows)
        {
            if (!dict.TryGetValue(p.ExperienceId, out var list))
            {
                list = [];
                dict[p.ExperienceId] = list;
            }
            list.Add(p);
        }
        return dict;
    }

    private static async Task<HashSet<long>> LoadLikedIdsAsync(MySqlConnection connection, string userId, IReadOnlyList<long> expIds, CancellationToken ct)
    {
        const string sql = "SELECT experience_id FROM experience_likes WHERE user_id = @userId AND experience_id IN @expIds";
        var rows = await connection.QueryAsync<long>(new CommandDefinition(sql, new { userId, expIds }, cancellationToken: ct));
        return rows.ToHashSet();
    }

    private static ExperienceView MapView(ExperienceRow r, IReadOnlyList<ExperiencePhotoView> photos, bool hasLiked)
    {
        return new ExperienceView(
            r.Id, r.RestaurantId, r.RestaurantName, r.RestaurantCity, r.UserId, r.AuthorName,
            r.Stars, r.ServiceStars, r.FlavorStars, r.AmbianceStars, r.CostBenefitStars,
            r.ReviewText, r.DishesConsumed, r.PricePaid, r.Visibility, r.UnlistedToken,
            r.Status, r.RejectionReason, r.AppealStatus, r.AppealText, r.LikesCount, r.CommentsCount,
            photos, r.CreatedAt, r.PublishedAt, hasLiked);
    }

    private sealed class ExperienceRow
    {
        public long Id { get; set; }
        public long RestaurantId { get; set; }
        public string RestaurantName { get; set; } = "";
        public string RestaurantCity { get; set; } = "";
        public string UserId { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public int Stars { get; set; }
        public int ServiceStars { get; set; }
        public int FlavorStars { get; set; }
        public int AmbianceStars { get; set; }
        public int CostBenefitStars { get; set; }
        public string? ReviewText { get; set; }
        public string? DishesConsumed { get; set; }
        public decimal? PricePaid { get; set; }
        public string Visibility { get; set; } = "";
        public string? UnlistedToken { get; set; }
        public string Status { get; set; } = "";
        public string? RejectionReason { get; set; }
        public string AppealStatus { get; set; } = "";
        public string? AppealText { get; set; }
        public int LikesCount { get; set; }
        public int CommentsCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
