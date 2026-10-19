using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;

namespace BelemRestaurantes.Web.Models;

public sealed class MapRenderTimingFilter(ILogger<MapRenderTimingFilter> logger) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var watch = Stopwatch.StartNew();
        await next();
        if (context.HttpContext.Request.Path == "/mapa")
            logger.LogInformation("Map Razor result rendered in {Milliseconds} ms", watch.Elapsed.TotalMilliseconds);
    }
}
