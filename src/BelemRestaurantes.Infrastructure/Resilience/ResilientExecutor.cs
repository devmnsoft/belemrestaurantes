using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Resilience;

namespace BelemRestaurantes.Infrastructure.Resilience;

/// <summary>
/// Executa uma operação sob retry, backoff e circuit breaker.
/// Combinados em um único ponto para que toda chamada externa tenha as três
/// proteções por padrão, sem que cada PROVIDER precise lembrá-las.
/// </summary>
public sealed class ResilientExecutor(
    IRetryPolicy policy,
    CircuitBreaker breaker,
    ILogger<ResilientExecutor> logger)
{
    private int _totalAttempts;
    private int _totalFailures;

    public int TotalAttempts => Volatile.Read(ref _totalAttempts);

    public int TotalFailures => Volatile.Read(ref _totalFailures);

    public async Task<T> ExecuteAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        if (!breaker.TryEnter())
        {
            throw new CircuitOpenException(operationName);
        }

        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            Interlocked.Increment(ref _totalAttempts);

            try
            {
                var result = await action(ct);
                breaker.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento do host não conta como falha do provedor.
                breaker.RecordSuccess();
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalFailures);
                breaker.RecordFailure();

                if (!policy.ShouldRetry(attempt, ex))
                {
                    throw;
                }

                var delay = policy.GetDelay(attempt);
                logger.LogWarning(
                    "Falha em {Operation} (tentativa {Attempt}/{Max}). Nova tentativa em {Delay:F1}s. Motivo: {Reason}",
                    operationName, attempt, policy.MaxAttempts, delay.TotalSeconds, ex.Message);

                await Task.Delay(delay, ct);
            }
        }
    }
}

public sealed class CircuitOpenException(string operation)
    : Exception($"Circuito aberto para '{operation}'; a chamada foi recusada sem hitting a API.");