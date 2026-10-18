namespace BelemRestaurantes.Application.Models;

/// <summary>Indisponibilidade externa: adiar o job sem consumir tentativas de processamento.</summary>
public sealed class DiscoveryProviderUnavailableException(
    string provider, DateTimeOffset retryAt, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Provider { get; } = provider;
    public DateTimeOffset RetryAt { get; } = retryAt;
}
