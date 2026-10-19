namespace BelemRestaurantes.Web;

public readonly record struct CuiaAskSnapshot(long Asks, long CatalogAnswers, long ServiceAnswers, long Failures, long TotalMilliseconds)
{
    public long AverageMilliseconds => Asks == 0 ? 0 : TotalMilliseconds / Asks;
}

public static class CuiaAskMetrics
{
    private static long _asks;
    private static long _catalog;
    private static long _service;
    private static long _failures;
    private static long _totalMs;

    public static void Record(string answerKind, TimeSpan elapsed)
    {
        Interlocked.Increment(ref _asks);
        if (string.Equals(answerKind, "servico", StringComparison.Ordinal))
            Interlocked.Increment(ref _service);
        else
            Interlocked.Increment(ref _catalog);
        Interlocked.Add(ref _totalMs, Math.Max(0, (long)elapsed.TotalMilliseconds));
    }

    public static void RecordFailure()
    {
        Interlocked.Increment(ref _asks);
        Interlocked.Increment(ref _failures);
    }

    public static CuiaAskSnapshot Read() => new(
        Interlocked.Read(ref _asks),
        Interlocked.Read(ref _catalog),
        Interlocked.Read(ref _service),
        Interlocked.Read(ref _failures),
        Interlocked.Read(ref _totalMs));
}
