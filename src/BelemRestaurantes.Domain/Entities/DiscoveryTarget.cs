using BelemRestaurantes.Domain.Enums;

namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Alvo de pesquisa recorrente (cidade + categoria). É o que o operador pausa
/// ou reativa pelo painel, e o que a fila consome.
/// </summary>
public sealed class DiscoveryTarget
{
    public long Id { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset NextRunAt { get; private set; }

    /// <summary>Instante até onde outro ciclo detém este alvo (anti-duplicata).</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public int Attempts { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public string? LastError { get; private set; }

    public DiscoveryTarget(
        long id,
        string city,
        string category,
        bool enabled,
        DateTimeOffset nextRunAt,
        DateTimeOffset? lockedUntil = null,
        int attempts = 0,
        DateTimeOffset? lastSuccessAt = null,
        string? lastError = null)
    {
        Id = id;
        City = city;
        Category = category;
        Enabled = enabled;
        NextRunAt = nextRunAt;
        LockedUntil = lockedUntil;
        Attempts = attempts;
        LastSuccessAt = lastSuccessAt;
        LastError = lastError;
    }

    public bool IsDueAt(DateTimeOffset now) =>
        Enabled && NextRunAt <= now && (LockedUntil is null || LockedUntil <= now);

    /// <summary>Um alvo pausado nunca entra na fila, mesmo que esteja vencido.</summary>
    public void Pause() => Enabled = false;

    public void Resume(DateTimeOffset now)
    {
        Enabled = true;
        NextRunAt = now;
    }

    /// <summary>
    /// Calcula o próximo disparo com backoff exponencial limitado por <paramref name="cap"/>.
    /// Usado tanto para sucesso (intervalo normal) quanto para erro (intervalo crescente).
    /// </summary>
    public static TimeSpan ComputeBackoff(int attempt, TimeSpan baseDelay, TimeSpan cap)
    {
        if (attempt <= 0)
        {
            return baseDelay;
        }

        // 2^(attempt-1) limitado para evitar overflow em tentativas altas.
        var exponent = Math.Min(attempt - 1, 20);
        var multiplier = 1L << exponent;
        var ticks = baseDelay.Ticks * multiplier;
        return ticks >= cap.Ticks ? cap : TimeSpan.FromTicks(ticks);
    }
}