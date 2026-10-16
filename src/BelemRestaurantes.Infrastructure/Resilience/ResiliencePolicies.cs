using System.Collections.Concurrent;

namespace BelemRestaurantes.Infrastructure.Resilience;

/// <summary>Classifica falhas para decidir se vale a pena tentar de novo.</summary>
public interface IRetryPolicy
{
    /// <summary>Teto de tentativas, usado em log e em decisão de descarte.</summary>
    int MaxAttempts { get; }

    /// <summary>True quando a falha é transitória e outra tentativa faz sentido.</summary>
    bool ShouldRetry(int attempt, Exception error);

    /// <summary>Espera antes da próxima tentativa, com jitter para dispersar a carga.</summary>
    TimeSpan GetDelay(int attempt);
}

/// <summary>
/// Política com backoff exponencial e jitter completo.
/// Sem jitter, N instâncias que falharam juntas voltariam a falhar juntas.
/// </summary>
public sealed class ExponentialBackoffPolicy(
    int maxAttempts = 3,
    TimeSpan? baseDelay = null,
    TimeSpan? maxDelay = null) : IRetryPolicy
{
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
    private readonly TimeSpan _maxDelay = maxDelay ?? TimeSpan.FromSeconds(60);
    private readonly Random _jitter = Random.Shared;

    public int MaxAttempts { get; } = Math.Max(1, maxAttempts);

    public bool ShouldRetry(int attempt, Exception error)
    {
        if (attempt >= MaxAttempts)
        {
            return false;
        }

        return error switch
        {
            // Erros de regra de negócio nunca são transitórios.
            OperationCanceledException => false,
            _ => true
        };
    }

    public TimeSpan GetDelay(int attempt)
    {
        var exponent = Math.Min(attempt, 16);
        var raw = _baseDelay.Ticks * (1L << exponent);

        // Full jitter: escolhe entre 0 e o teto exponencial.
        var capped = raw >= _maxDelay.Ticks ? _maxDelay.Ticks : raw;
        var jittered = (long)(_jitter.NextDouble() * capped);

        return TimeSpan.FromTicks(Math.Max(jittered, 100));
    }
}

/// <summary>Estado de um circuito.</summary>
public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}

/// <summary>
/// Circuit breaker por provedor. Após N falhas seguidas abre e deixa passar uma
/// única chamada de teste; se ela passar, fecha de novo.
///
/// Impede que uma API fora do ar consuma todos os slots do ciclo de descoberta
/// enquanto ainda bloqueia os ciclos seguintes.
/// </summary>
public sealed class CircuitBreaker(int failureThreshold = 5, TimeSpan? breakDuration = null, IClock? clock = null)
{
    private readonly int _failureThreshold = Math.Max(1, failureThreshold);
    private readonly TimeSpan _breakDuration = breakDuration ?? TimeSpan.FromMinutes(2);
    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;
    private bool _isProbeInFlight;

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                if (_consecutiveFailures < _failureThreshold)
                {
                    return CircuitState.Closed;
                }

                return _clock.UtcNow - _openedAt >= _breakDuration
                    ? CircuitState.HalfOpen
                    : CircuitState.Open;
            }
        }
    }

    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate)
            {
                return _consecutiveFailures;
            }
        }
    }

    /// <summary>
    /// True se a chamada pode seguir. Quando o circuito está aberto, apenas uma
    /// requisição de sondagem é liberada por vez.
    /// </summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_consecutiveFailures < _failureThreshold)
            {
                return true;
            }

            if (_clock.UtcNow - _openedAt < _breakDuration)
            {
                return false;
            }

            if (_isProbeInFlight)
            {
                return false;
            }

            _isProbeInFlight = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _isProbeInFlight = false;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            _isProbeInFlight = false;

            if (_consecutiveFailures >= _failureThreshold)
            {
                _openedAt = _clock.UtcNow;
            }
        }
    }
}

/// <summary>Registro simples dos contadores usados pelo dashboard.</summary>
public sealed class ProviderCallCounter
{
    private readonly ConcurrentDictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    public int Increment(string key)
    {
        var updated = _counters.AddOrUpdate(key, 1, (_, current) => current + 1);
        return updated;
    }

    public int Get(string key) => _counters.TryGetValue(key, out var value) ? value : 0;

    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_counters, StringComparer.OrdinalIgnoreCase);
}