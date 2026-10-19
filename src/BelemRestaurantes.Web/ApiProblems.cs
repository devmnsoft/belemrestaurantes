using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web;

public static class ApiProblems
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

    public static IActionResult Result(HttpContext http, int status, string code, string message, bool retryable) =>
        new JsonResult(Body(http, status, code, message, retryable))
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };

    public static async Task WriteAsync(HttpContext http, int status, string code, string message, bool retryable, CancellationToken ct = default)
    {
        if (http.Response.HasStarted) return;
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json; charset=utf-8";
        await http.Response.WriteAsync(JsonSerializer.Serialize(Body(http, status, code, message, retryable), Json), ct);
    }

    public static Task HandleRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (!IsApi(context.Request))
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }

        var denied = status == StatusCodes.Status403Forbidden;
        return WriteAsync(
            context.HttpContext,
            status,
            denied ? "forbidden" : "unauthorized",
            denied
                ? "Esta consulta não está disponível."
                : "Não conseguimos concluir sua pesquisa agora. Tente novamente.",
            !denied);
    }

    public static (int Status, string Code, string Message, bool Retryable) Classify(Exception exception) => exception switch
    {
        AntiforgeryValidationException => (StatusCodes.Status400BadRequest, "antiforgery", "Não foi possível confirmar o envio. Atualize a página e tente novamente.", true),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_body", "Não conseguimos concluir sua pesquisa agora. Tente novamente.", true),
        System.Data.Common.DbException => (StatusCodes.Status503ServiceUnavailable, "catalog_unavailable", "O serviço de recomendações está temporariamente indisponível.", true),
        TimeoutException => (StatusCodes.Status504GatewayTimeout, "timeout", "A consulta demorou mais do que o esperado.", true),
        HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "provider_unavailable", "O serviço de recomendações está temporariamente indisponível.", true),
        _ => (StatusCodes.Status500InternalServerError, "unexpected", "Não conseguimos concluir sua pesquisa agora. Tente novamente.", true)
    };

    private static object Body(HttpContext http, int status, string code, string message, bool retryable) => new
    {
        type = "urn:belemrestaurantes:" + code,
        title = message,
        status,
        detail = message,
        code,
        correlationId = http.TraceIdentifier,
        retryable,
        error = message
    };
}
