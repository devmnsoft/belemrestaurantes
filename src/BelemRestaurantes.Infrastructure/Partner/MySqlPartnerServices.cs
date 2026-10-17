using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Partner;

public sealed class MySqlPartnerAuthorizationService(MySqlConnectionFactory connectionFactory) : IPartnerAuthorizationService
{
    public async Task<bool> IsOwnerOrManagerAsync(long restaurantId, string userId, CancellationToken ct)
    {
        if (restaurantId <= 0 || string.IsNullOrWhiteSpace(userId)) return false;
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT COUNT(*) FROM restaurant_owners WHERE restaurant_id = @restaurantId AND user_id = @userId";
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { restaurantId, userId }, cancellationToken: ct));
        return count > 0;
    }
}

public sealed class MySqlPartnerPortalService(
    MySqlConnectionFactory connectionFactory,
    ILogger<MySqlPartnerPortalService> logger) : IPartnerPortalService
{
    public async Task<long> SubmitClaimRequestAsync(RestaurantClaimRequest request, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_claim_requests
            (restaurant_id, user_id, business_name, tax_id, contact_phone, contact_email, official_channels, verification_evidence, status, created_at, updated_at)
            VALUES
            (@RestaurantId, @UserId, @BusinessName, @TaxId, @ContactPhone, @ContactEmail, @OfficialChannels, @VerificationEvidence, 'pending', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();";

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, request, cancellationToken: ct));
    }

    public async Task<RestaurantClaimRequest?> GetClaimRequestAsync(long restaurantId, string userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            SELECT r.*, res.name as RestaurantName
            FROM restaurant_claim_requests r
            JOIN restaurants res ON res.id = r.restaurant_id
            WHERE r.restaurant_id = @restaurantId AND r.user_id = @userId
            ORDER BY r.id DESC LIMIT 1;";
        return await connection.QueryFirstOrDefaultAsync<RestaurantClaimRequest>(new CommandDefinition(sql, new { restaurantId, userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantClaimRequest>> ListPendingClaimsAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            SELECT r.*, res.name as RestaurantName
            FROM restaurant_claim_requests r
            JOIN restaurants res ON res.id = r.restaurant_id
            WHERE r.status = 'pending'
            ORDER BY r.created_at ASC;";
        var list = await connection.QueryAsync<RestaurantClaimRequest>(new CommandDefinition(sql, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<bool> ReviewClaimAsync(long claimId, bool approve, string? rejectionReason, string reviewedBy, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            const string getClaimSql = "SELECT * FROM restaurant_claim_requests WHERE id = @claimId FOR UPDATE;";
            var claim = await connection.QueryFirstOrDefaultAsync<RestaurantClaimRequest>(new CommandDefinition(getClaimSql, new { claimId }, transaction, cancellationToken: ct));
            if (claim == null || claim.Status != "pending") return false;

            var newStatus = approve ? "approved" : "rejected";
            const string updateClaimSql = @"
                UPDATE restaurant_claim_requests
                SET status = @newStatus, rejection_reason = @rejectionReason, reviewed_by = @reviewedBy, reviewed_at = UTC_TIMESTAMP(6), updated_at = UTC_TIMESTAMP(6)
                WHERE id = @claimId;";

            await connection.ExecuteAsync(new CommandDefinition(updateClaimSql, new { claimId, newStatus, rejectionReason, reviewedBy }, transaction, cancellationToken: ct));

            if (approve)
            {
                const string addOwnerSql = @"
                    INSERT INTO restaurant_owners (restaurant_id, user_id, role, created_at)
                    VALUES (@RestaurantId, @UserId, 'owner', UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE role = 'owner';";
                await connection.ExecuteAsync(new CommandDefinition(addOwnerSql, new { claim.RestaurantId, claim.UserId }, transaction, cancellationToken: ct));

                // Inicializar perfil estendido e plano gratuito se ainda não existirem
                const string initProfileSql = @"
                    INSERT IGNORE INTO restaurant_profiles (restaurant_id, commercial_phone, commercial_email)
                    VALUES (@RestaurantId, @ContactPhone, @ContactEmail);";
                await connection.ExecuteAsync(new CommandDefinition(initProfileSql, new { claim.RestaurantId, claim.ContactPhone, claim.ContactEmail }, transaction, cancellationToken: ct));

                const string initSubSql = @"
                    INSERT IGNORE INTO restaurant_subscriptions (restaurant_id, user_id, plan_code, status, started_at)
                    VALUES (@RestaurantId, @UserId, 'free', 'active', UTC_TIMESTAMP(6));";
                await connection.ExecuteAsync(new CommandDefinition(initSubSql, new { claim.RestaurantId, claim.UserId }, transaction, cancellationToken: ct));
            }

            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Erro ao revisar reivindicação de restaurante {ClaimId}", claimId);
            throw;
        }
    }

    public async Task<IReadOnlyList<Restaurant>> ListOwnedRestaurantsAsync(string userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            SELECT r.*
            FROM restaurants r
            JOIN restaurant_owners o ON o.restaurant_id = r.id
            WHERE o.user_id = @userId
            ORDER BY r.name ASC;";
        var list = await connection.QueryAsync<Restaurant>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<RestaurantProfile?> GetRestaurantProfileAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM restaurant_profiles WHERE restaurant_id = @restaurantId;";
        return await connection.QueryFirstOrDefaultAsync<RestaurantProfile>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
    }

    public async Task<bool> UpdateRestaurantProfileAsync(long restaurantId, RestaurantProfile profile, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_profiles
            (restaurant_id, official_description, slogan, history, commercial_phone, commercial_whatsapp, commercial_email,
             delivery_whatsapp, delivery_ifood_url, delivery_custom_url, is_accepting_orders, has_wifi, has_parking,
             has_outdoor_seating, is_pet_friendly, is_accessible, updated_at)
            VALUES
            (@restaurantId, @OfficialDescription, @Slogan, @History, @CommercialPhone, @CommercialWhatsapp, @CommercialEmail,
             @DeliveryWhatsapp, @DeliveryIfoodUrl, @DeliveryCustomUrl, @IsAcceptingOrders, @HasWifi, @HasParking,
             @HasOutdoorSeating, @IsPetFriendly, @IsAccessible, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
             official_description = VALUES(official_description), slogan = VALUES(slogan), history = VALUES(history),
             commercial_phone = VALUES(commercial_phone), commercial_whatsapp = VALUES(commercial_whatsapp),
             commercial_email = VALUES(commercial_email), delivery_whatsapp = VALUES(delivery_whatsapp),
             delivery_ifood_url = VALUES(delivery_ifood_url), delivery_custom_url = VALUES(delivery_custom_url),
             is_accepting_orders = VALUES(is_accepting_orders), has_wifi = VALUES(has_wifi), has_parking = VALUES(has_parking),
             has_outdoor_seating = VALUES(has_outdoor_seating), is_pet_friendly = VALUES(is_pet_friendly),
             is_accessible = VALUES(is_accessible), updated_at = UTC_TIMESTAMP(6);";

        profile.RestaurantId = restaurantId;
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, profile, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<(IReadOnlyList<RestaurantSchedule> Regular, IReadOnlyList<RestaurantSpecialSchedule> Special)> GetSchedulesAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string regularSql = "SELECT * FROM restaurant_schedules WHERE restaurant_id = @restaurantId ORDER BY day_of_week ASC;";
        const string specialSql = "SELECT * FROM restaurant_special_schedules WHERE restaurant_id = @restaurantId AND specific_date >= CURRENT_DATE() ORDER BY specific_date ASC;";

        var regular = (await connection.QueryAsync<RestaurantSchedule>(new CommandDefinition(regularSql, new { restaurantId }, cancellationToken: ct))).ToList();
        var special = (await connection.QueryAsync<RestaurantSpecialSchedule>(new CommandDefinition(specialSql, new { restaurantId }, cancellationToken: ct))).ToList();

        return (regular, special);
    }

    public async Task<bool> UpdateSchedulesAsync(long restaurantId, IEnumerable<RestaurantSchedule> regular, IEnumerable<RestaurantSpecialSchedule> special, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            const string delRegular = "DELETE FROM restaurant_schedules WHERE restaurant_id = @restaurantId;";
            await connection.ExecuteAsync(new CommandDefinition(delRegular, new { restaurantId }, transaction, cancellationToken: ct));

            const string insertRegular = @"
                INSERT INTO restaurant_schedules (restaurant_id, day_of_week, is_closed, open_time, close_time)
                VALUES (@restaurantId, @DayOfWeek, @IsClosed, @OpenTime, @CloseTime);";

            foreach (var r in regular)
            {
                await connection.ExecuteAsync(new CommandDefinition(insertRegular, new { restaurantId, r.DayOfWeek, r.IsClosed, r.OpenTime, r.CloseTime }, transaction, cancellationToken: ct));
            }

            const string insertSpecial = @"
                INSERT INTO restaurant_special_schedules (restaurant_id, specific_date, is_closed, open_time, close_time, reason)
                VALUES (@restaurantId, @SpecificDate, @IsClosed, @OpenTime, @CloseTime, @Reason)
                ON DUPLICATE KEY UPDATE is_closed = VALUES(is_closed), open_time = VALUES(open_time), close_time = VALUES(close_time), reason = VALUES(reason);";

            foreach (var s in special)
            {
                await connection.ExecuteAsync(new CommandDefinition(insertSpecial, new { restaurantId, s.SpecificDate, s.IsClosed, s.OpenTime, s.CloseTime, s.Reason }, transaction, cancellationToken: ct));
            }

            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Erro ao atualizar horários do restaurante {RestaurantId}", restaurantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<RestaurantStat>> GetStatsAsync(long restaurantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            SELECT * FROM restaurant_stats
            WHERE restaurant_id = @restaurantId AND stat_date >= @from AND stat_date <= @to
            ORDER BY stat_date ASC;";
        var list = await connection.QueryAsync<RestaurantStat>(new CommandDefinition(sql, new { restaurantId, from, to }, cancellationToken: ct));
        return list.ToList();
    }

    public async Task RecordStatClickAsync(long restaurantId, string clickType, CancellationToken ct)
    {
        if (restaurantId <= 0) return;
        await using var connection = await connectionFactory.OpenAsync(ct);
        var column = clickType.ToLowerInvariant() switch
        {
            "whatsapp" => "whatsapp_clicks",
            "route" => "route_clicks",
            "menu" => "menu_views",
            _ => "page_views"
        };

        var sql = $@"
            INSERT INTO restaurant_stats (restaurant_id, stat_date, {column})
            VALUES (@restaurantId, CURRENT_DATE(), 1)
            ON DUPLICATE KEY UPDATE {column} = {column} + 1;";

        await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantAnnouncement>> ListAnnouncementsAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM restaurant_announcements WHERE restaurant_id = @restaurantId ORDER BY created_at DESC;";
        var list = await connection.QueryAsync<RestaurantAnnouncement>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<long> CreateAnnouncementAsync(RestaurantAnnouncement announcement, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_announcements (restaurant_id, title, description, image_url, event_date, is_active, created_at)
            VALUES (@RestaurantId, @Title, @Description, @ImageUrl, @EventDate, @IsActive, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();";
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, announcement, cancellationToken: ct));
    }

    public async Task<bool> DeleteAnnouncementAsync(long restaurantId, long announcementId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "DELETE FROM restaurant_announcements WHERE restaurant_id = @restaurantId AND id = @announcementId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId, announcementId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ReplyToReviewAsync(long restaurantId, long experienceId, string userId, string replyText, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_review_replies (experience_id, restaurant_id, user_id, reply_text, created_at, updated_at)
            VALUES (@experienceId, @restaurantId, @userId, @replyText, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE reply_text = VALUES(reply_text), updated_at = UTC_TIMESTAMP(6);";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { experienceId, restaurantId, userId, replyText }, cancellationToken: ct)) > 0;
    }

    public async Task<RestaurantReviewReply?> GetReviewReplyAsync(long experienceId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM restaurant_review_replies WHERE experience_id = @experienceId;";
        return await connection.QueryFirstOrDefaultAsync<RestaurantReviewReply>(new CommandDefinition(sql, new { experienceId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RestaurantSocialMedia>> ListSocialMediasAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM restaurant_social_media WHERE restaurant_id = @restaurantId ORDER BY id ASC;";
        var list = await connection.QueryAsync<RestaurantSocialMedia>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<bool> UpsertSocialMediaAsync(long restaurantId, RestaurantSocialMedia media, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_social_media (restaurant_id, platform, profile_url, embed_code, is_verified, created_at)
            VALUES (@restaurantId, @Platform, @ProfileUrl, @EmbedCode, @IsVerified, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE profile_url = VALUES(profile_url), embed_code = VALUES(embed_code), is_verified = VALUES(is_verified);";
        media.RestaurantId = restaurantId;
        return await connection.ExecuteAsync(new CommandDefinition(sql, media, cancellationToken: ct)) > 0;
    }
}
