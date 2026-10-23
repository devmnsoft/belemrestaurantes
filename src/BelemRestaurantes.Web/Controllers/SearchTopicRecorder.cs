using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using Microsoft.Extensions.Caching.Memory;
namespace BelemRestaurantes.Web.Controllers;
internal static class SearchTopicRecorder
{
    public static async Task RecordAsync(HttpContext context,ISearchArchive archive,string query,CancellationToken ct)
    {
        if(context.Connection.RemoteIpAddress is null)return; // In-memory test hosts do not represent visitors.
        var topics=SearchTopics.Extract(query);if(topics.Count==0)return;
        var cache=context.RequestServices.GetRequiredService<IMemoryCache>();
        var session=CuiaSessionIdentity.GetOrCreate(context);
        foreach(var topic in topics)
        {
            var key="search-topic:"+session+":"+topic;
            if(cache.TryGetValue(key,out _))continue;
            cache.Set(key,true,TimeSpan.FromMinutes(30));
            try{await archive.RecordTopicsAsync(topic,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex){context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SearchTopics").LogWarning("Contagem de assuntos indisponível: {Type}",ex.GetType().Name);}
        }
    }
}
