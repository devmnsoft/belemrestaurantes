using System.Diagnostics;
using System.Text;

namespace BelemRestaurantes.Web.Diagnostics;

/// <summary>One boundary for unhandled application failures; parser rejections are logged by Kestrel.</summary>
public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        int? failureStatus = null;
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            failureStatus = 499;
            logger.LogInformation("Requisição cancelada pelo cliente. CorrelationId {CorrelationId}.", context.TraceIdentifier);
            throw;
        }
        catch (Exception ex)
        {
            failureStatus = StatusCodes.Status500InternalServerError;
            // Do not serialize messages, query strings, headers, cookies or bodies: they can contain credentials.
            logger.LogError("Exceção não tratada {ExceptionType}. CorrelationId {CorrelationId}. Stack {ExceptionStack}", ex.GetType().Name, context.TraceIdentifier, ex.StackTrace);
            throw; // Existing HTML/API exception handlers retain their status codes and response contracts.
        }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var bytes = context.Request.Headers.Sum(header => Encoding.UTF8.GetByteCount(header.Key) + Encoding.UTF8.GetByteCount(header.Value.ToString()) + 4);
                var cookieBytes = Encoding.UTF8.GetByteCount(context.Request.Headers.Cookie.ToString());
                logger.LogInformation("HTTP {Method} terminou com {StatusCode}; cabeçalhos {HeaderBytes} bytes, cookies {CookieBytes} bytes, {ElapsedMs} ms. CorrelationId {CorrelationId}.",
                    context.Request.Method, failureStatus ?? context.Response.StatusCode, bytes, cookieBytes, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
            }
        }
    }
}
