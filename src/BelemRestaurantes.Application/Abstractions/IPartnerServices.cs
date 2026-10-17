using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public interface IPartnerAuthorizationService
{
    Task<bool> IsOwnerOrManagerAsync(long restaurantId, string userId, CancellationToken ct);
}

public interface IPartnerPortalService
{
    Task<long> SubmitClaimRequestAsync(RestaurantClaimRequest request, CancellationToken ct);
    Task<RestaurantClaimRequest?> GetClaimRequestAsync(long restaurantId, string userId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantClaimRequest>> ListPendingClaimsAsync(CancellationToken ct);
    Task<bool> ReviewClaimAsync(long claimId, bool approve, string? rejectionReason, string reviewedBy, CancellationToken ct);
    Task<IReadOnlyList<Restaurant>> ListOwnedRestaurantsAsync(string userId, CancellationToken ct);
    Task<RestaurantProfile?> GetRestaurantProfileAsync(long restaurantId, CancellationToken ct);
    Task<bool> UpdateRestaurantProfileAsync(long restaurantId, RestaurantProfile profile, CancellationToken ct);
    Task<(IReadOnlyList<RestaurantSchedule> Regular, IReadOnlyList<RestaurantSpecialSchedule> Special)> GetSchedulesAsync(long restaurantId, CancellationToken ct);
    Task<bool> UpdateSchedulesAsync(long restaurantId, IEnumerable<RestaurantSchedule> regular, IEnumerable<RestaurantSpecialSchedule> special, CancellationToken ct);
    Task<IReadOnlyList<RestaurantStat>> GetStatsAsync(long restaurantId, DateOnly from, DateOnly to, CancellationToken ct);
    Task RecordStatClickAsync(long restaurantId, string clickType, CancellationToken ct);
    Task<IReadOnlyList<RestaurantAnnouncement>> ListAnnouncementsAsync(long restaurantId, CancellationToken ct);
    Task<long> CreateAnnouncementAsync(RestaurantAnnouncement announcement, CancellationToken ct);
    Task<bool> DeleteAnnouncementAsync(long restaurantId, long announcementId, CancellationToken ct);
    Task<bool> ReplyToReviewAsync(long restaurantId, long experienceId, string userId, string replyText, CancellationToken ct);
    Task<RestaurantReviewReply?> GetReviewReplyAsync(long experienceId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantSocialMedia>> ListSocialMediasAsync(long restaurantId, CancellationToken ct);
    Task<bool> UpsertSocialMediaAsync(long restaurantId, RestaurantSocialMedia media, CancellationToken ct);
}

public interface IMenuService
{
    Task<IReadOnlyList<MenuCategory>> GetRestaurantMenuAsync(long restaurantId, CancellationToken ct);
    Task<long> AddCategoryAsync(long restaurantId, string name, int displayOrder, CancellationToken ct);
    Task<bool> UpdateCategoryAsync(long restaurantId, long categoryId, string name, int displayOrder, bool isActive, CancellationToken ct);
    Task<bool> DeleteCategoryAsync(long restaurantId, long categoryId, CancellationToken ct);
    Task<long> AddMenuItemAsync(MenuItem item, CancellationToken ct);
    Task<bool> UpdateMenuItemAsync(MenuItem item, CancellationToken ct);
    Task<bool> DeleteMenuItemAsync(long restaurantId, long itemId, CancellationToken ct);
    Task<string> SuggestDishDescriptionWithAiAsync(string dishName, string? ingredients, CancellationToken ct);
}

public interface IMonetizationService
{
    Task<IReadOnlyList<CommercialPlan>> ListPlansAsync(CancellationToken ct);
    Task<RestaurantSubscription?> GetActiveSubscriptionAsync(long restaurantId, CancellationToken ct);
    Task<MerchantOrder> CreateOrderAsync(long restaurantId, string userId, string planCode, CancellationToken ct);
    Task<bool> ConfirmPaymentAsync(string orderNumber, CancellationToken ct);
    Task<IReadOnlyList<RestaurantCoupon>> ListCouponsAsync(long restaurantId, CancellationToken ct);
    Task<long> CreateCouponAsync(RestaurantCoupon coupon, CancellationToken ct);
    Task<bool> ToggleCouponAsync(long restaurantId, long couponId, bool isActive, CancellationToken ct);
}

public interface IMobilityTrailService
{
    Task<IReadOnlyList<GastronomicTrail>> ListTrailsAsync(CancellationToken ct);
    Task<GastronomicTrail?> GetTrailBySlugAsync(string slug, CancellationToken ct);
    Task<string?> GetTransitDirectionsAsync(long restaurantId, CancellationToken ct);
}
