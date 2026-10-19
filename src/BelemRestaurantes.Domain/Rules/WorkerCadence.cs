namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// A fila pode ser conferida a cada minuto. A consulta externa segue o intervalo maior.
/// </summary>
public static class WorkerCadence
{
    public static TimeSpan QueueCheck(int? configuredMinutes) =>
        TimeSpan.FromMinutes(Math.Clamp(configuredMinutes ?? 1, 1, 60));

    public static bool ExternalDue(DateTimeOffset now, DateTimeOffset? lastExternal, TimeSpan interval) =>
        lastExternal is null || now - lastExternal.Value >= interval;
}
