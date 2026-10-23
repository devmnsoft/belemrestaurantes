using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
namespace BelemRestaurantes.Worker;
public static class ReferenceFeedCommand
{
    public static async Task RunAsync(IServiceProvider provider,string url,string city)
    {
        using var scope=provider.CreateScope();var services=scope.ServiceProvider;
        if(ExternalContentRules.CanonicalUrl(url) is null)throw new ArgumentException("Feed HTTPS público necessário.");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));var ct=deadline.Token;
        var allowed=await services.GetRequiredService<IRobotsTxtService>().CheckAccessAsync(url,"CuiaCrawler",ct);
        if(!allowed.IsAllowed){Console.WriteLine("Feed não configurado: acesso automatizado não confirmado pela origem.");return;}
        if(allowed.CrawlDelaySeconds>10){Console.WriteLine("Feed não configurado: cadência da fonte exige revisão operacional.");return;}
        if(allowed.CrawlDelaySeconds>0)await Task.Delay(TimeSpan.FromSeconds(allowed.CrawlDelaySeconds),ct);
        var download=await services.GetRequiredService<ISafeHttpClient>().DownloadAsync(url,timeoutSeconds:10,ct:ct);
        if(!download.Success||download.ContentText is null){Console.WriteLine("Feed não configurado: "+download.ErrorMessage);return;}
        var entries=services.GetRequiredService<ICrawlerParser>().ParseRssFeed(download.ContentText,url);
        if(entries.Count==0){Console.WriteLine("Feed não configurado: não retornou itens RSS/Atom válidos.");return;}
        var sources=services.GetRequiredService<ICrawlerSourceRepository>();var source=await sources.GetByBaseUrlAsync(url,ct);
        if(source is not null){Console.WriteLine("Fonte já cadastrada; configuração e pausas preservadas.");return;}
        var id=await sources.CreateSourceAsync(new(){Name="Referências · "+new Uri(url).Host,BaseUrl=url,SourceType="rss_feed",ContentOnly=true,TargetCity=city,Status="active",CrawlDelaySeconds=Math.Max(1,allowed.CrawlDelaySeconds)},ct);
        var published=0;
        foreach(var entry in entries.Where(e=>ExternalContentRules.Relevant(e.Title,city)&&ExternalContentRules.Regional(e.Title+" "+e.Description,city)).Take(20))
            if(await services.GetRequiredService<IExternalContent>().ObserveAsync(new(){Title=entry.Title,Url=entry.Url,City=city,Summary=entry.Description??"",VideoUrl=ExternalContentRules.FindVideoLink(entry.Description??""),ObservedAt=DateTime.UtcNow},ct) is not null)published++;
        await services.GetRequiredService<ICrawlerTaskQueue>().EnqueueTaskAsync(new(){SourceId=id,Url=url,City=city},ct);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{sourceId=id,contentOnly=true,feedItems=entries.Count,publishedReferences=published}));
    }
}
