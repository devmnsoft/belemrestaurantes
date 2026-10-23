using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Application.Abstractions;

public interface IEditorialTrails
{
    Task<IReadOnlyList<GastronomicTrail>> ListForEditingAsync(CancellationToken ct);
    Task<GastronomicTrail?> GetForEditingAsync(int id, CancellationToken ct);
    Task<bool> SaveDraftAsync(int id, string title, string description, int durationHours, TrailEditorial editorial, int version, string actor, CancellationToken ct);
    Task<bool> SetPublishedAsync(int id, bool published, int version, string actor, CancellationToken ct);
}
