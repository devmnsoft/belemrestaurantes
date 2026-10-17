using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record RestaurantGallery(
    IReadOnlyList<ExperiencePhotoView> OfficialPhotos,
    IReadOnlyList<ExperiencePhotoView> CommunityPhotos,
    IReadOnlyList<ExperiencePhotoView> IllustrativePhotos
);

public interface IPhotoStorageService
{
    Task<ExperiencePhoto> ProcessAndSaveAsync(
        Stream inputStream,
        string originalFileName,
        string contentType,
        string userId,
        long restaurantId,
        long experienceId,
        string? caption,
        CancellationToken ct
    );

    Task<(Stream Stream, string ContentType, string FileName)?> GetPhotoFileAsync(
        string fileName,
        string? viewerUserId,
        bool isThumb,
        CancellationToken ct
    );

    Task<RestaurantGallery> GetGalleryForRestaurantAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<ExperiencePhoto>> ListPendingPhotosAsync(int page, int pageSize, CancellationToken ct);
    Task<bool> ModeratePhotoAsync(long photoId, string newStatus, string moderatorActor, CancellationToken ct);
    Task<bool> DeletePhotoAsync(long photoId, string userId, bool isAdmin, CancellationToken ct);
}
