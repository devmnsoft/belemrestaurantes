using BelemRestaurantes.Domain.Enums;

namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Item persistido da fila de descoberta. Sobrevive a reinício do serviço: o
/// worker retoma o que ficou <see cref="DiscoveryJobState.Running"/> além do prazo.
/// </summary>
public sealed class DiscoveryJob
{
    public long Id { get; private set; }
    public long TargetId { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;

    public DiscoveryJobState State { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Nome da instância que deteve o job; usado para diagnóstico de concorrência.</summary>
    public string? LockedBy { get; private set; }

    public DiscoveryJob(
        long id,
        long targetId,
        string city,
        string category,
        DiscoveryJobState state,
        int attempts,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? lockedUntil = null,
        string? lockedBy = null,
        DateTimeOffset? completedAt = null,
        string? lastError = null)
    {
        Id = id;
        TargetId = targetId;
        City = city;
        Category = category;
        State = state;
        Attempts = attempts;
        NextAttemptAt = nextAttemptAt;
        LockedUntil = lockedUntil;
        LockedBy = lockedBy;
        CompletedAt = completedAt;
        LastError = lastError;
    }

    public static DiscoveryJob CreatePending(long targetId, string city, string category, DateTimeOffset now) =>
        new(0, targetId, city, category, DiscoveryJobState.Pending, 0, now);

    public bool IsDueAt(DateTimeOffset now) =>
        State is DiscoveryJobState.Pending && NextAttemptAt <= now;

    public void MarkRunning(DateTimeOffset now, TimeSpan lockDuration, string instanceId)
    {
        State = DiscoveryJobState.Running;
        LockedUntil = now.Add(lockDuration);
        LockedBy = instanceId;
        LastError = null;
    }

    public void MarkSucceeded(DateTimeOffset now)
    {
        State = DiscoveryJobState.Succeeded;
        CompletedAt = now;
        LockedUntil = null;
        LockedBy = null;
        LastError = null;
    }

    /// <summary>
    /// Registra uma falha. Devolve <c>false</c> quando as tentativas acabaram e o
    /// job foi para dead-letter, exigindo intervenção do operador.
    /// </summary>
    public bool MarkFailed(string error, DateTimeOffset now, TimeSpan backoff, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        State = Attempts >= maxAttempts ? DiscoveryJobState.DeadLettered : DiscoveryJobState.Pending;
        NextAttemptAt = State == DiscoveryJobState.Pending ? now.Add(backoff) : now;
        LockedUntil = null;
        LockedBy = null;
        return State != DiscoveryJobState.DeadLettered;
    }

    /// <summary>Devolve para a fila um job que ficou travado além do prazo (crash do worker).</summary>
    public void ReleaseStaleLock(DateTimeOffset now)
    {
        if (State is DiscoveryJobState.Running && (LockedUntil is null || LockedUntil <= now))
        {
            State = DiscoveryJobState.Pending;
            LockedUntil = null;
            LockedBy = null;
        }
    }
}