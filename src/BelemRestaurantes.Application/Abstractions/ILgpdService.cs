using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public sealed record LgpdExportData(
    string UserId,
    string FullName,
    string Email,
    DateTimeOffset AccountCreatedAt,
    IReadOnlyList<MemberSessionView> Sessions,
    IReadOnlyList<ExperienceView> Experiences,
    IReadOnlyList<CommentView> Comments,
    IReadOnlyList<long> FavoritedRestaurantIds,
    DateTimeOffset ExportGeneratedAt
);

public interface ILgpdService
{
    Task<long> RequestExportAsync(string userId, CancellationToken ct);
    Task<LgpdExportData> BuildExportDataAsync(string userId, CancellationToken ct);
    Task<long> RequestDeletionAsync(string userId, string? reason, CancellationToken ct);
    Task<bool> AnonymizeAccountAsync(string userId, string? adminActor, CancellationToken ct);
    Task<IReadOnlyList<LgpdRequest>> ListPendingRequestsAsync(CancellationToken ct);
    Task<bool> ReviewRequestAsync(long requestId, bool complete, string? notes, string adminActor, CancellationToken ct);
}
