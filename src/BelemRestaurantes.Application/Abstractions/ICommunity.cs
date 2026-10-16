namespace BelemRestaurantes.Application.Abstractions;

public interface IFavoriteRepository
{
    Task<IReadOnlyList<long>> ListAsync(string userSubject, CancellationToken ct);

    Task<bool> AddAsync(string userSubject, long restaurantId, CancellationToken ct);

    Task<bool> RemoveAsync(string userSubject, long restaurantId, CancellationToken ct);

    Task<bool> IsFavoriteAsync(string userSubject, long restaurantId, CancellationToken ct);
}

public interface IRatingRepository
{
    /// <summary>Registra/ atualiza a avaliação de um usuário; começa como pending para moderação.</summary>
    Task<long> UpsertAsync(string userSubject, long restaurantId, int stars, string? review, CancellationToken ct);

    Task<IReadOnlyList<RestaurantRatingView>> ListApprovedAsync(long restaurantId, CancellationToken ct);

    Task<double?> AverageAsync(long restaurantId, CancellationToken ct);
}

public sealed record RestaurantRatingView(long Id, string UserSubject, int Stars, string? Review, DateTimeOffset CreatedAt);

public interface IDeletionRequestRepository
{
    Task<long> CreateAsync(string requesterSubject, string targetType, long targetId, string? reason, CancellationToken ct);

    Task<IReadOnlyList<DeletionRequestView>> ListPendingAsync(CancellationToken ct);

    Task CompleteAsync(long id, string actor, CancellationToken ct);

    Task RejectAsync(long id, string actor, string reason, CancellationToken ct);
}

public sealed record DeletionRequestView(long Id, string RequesterSubject, string TargetType, long TargetId, string? Reason, string Status, DateTimeOffset RequestedAt);