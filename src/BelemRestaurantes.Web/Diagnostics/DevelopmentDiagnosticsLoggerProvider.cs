using System.Text.Json;

namespace BelemRestaurantes.Web.Diagnostics;

/// <summary>Bounded local diagnostics without header values, request bodies or exception messages.</summary>
public sealed class DevelopmentDiagnosticsLoggerProvider : ILoggerProvider
{
    private static readonly object Gate = new();
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MNSOFT", "BelemRestaurantes", "logs");
    private static readonly HashSet<string> AllowedFields = ["CorrelationId", "StatusCode", "HeaderBytes", "CookieBytes", "ElapsedMs", "Method", "ExceptionType", "ExceptionStack"];

    public ILogger CreateLogger(string categoryName) => new LocalLogger(categoryName);
    public void Dispose() { }
    public static void RecordStartupFailure(Exception exception) => Write("Startup", LogLevel.Critical, default, exception, null);

    private static void Write(string category, LogLevel level, EventId eventId, Exception? exception, object? state)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> entries
            ? entries.Where(pair => AllowedFields.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)
            : null;
        var row = JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.UtcNow, category, level = level.ToString(), eventId = eventId.Id,
            eventName = eventId.Name, exceptionType = exception?.GetType().FullName,
            status = (exception as BadHttpRequestException)?.StatusCode,
            stack = exception?.StackTrace, fields
        });
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"web-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, row + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Não foi possível gravar o diagnóstico local ({ex.GetType().Name}); consulte a saída do servidor.");
        }
    }

    private sealed class LocalLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning
            || (category == "Microsoft.AspNetCore.Server.Kestrel.BadRequests" && level >= LogLevel.Debug)
            || ((category == "Microsoft.Hosting.Lifetime" || category == typeof(RequestDiagnosticsMiddleware).FullName) && level >= LogLevel.Information);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) Write(category, level, eventId, exception, state);
        }
    }
}
