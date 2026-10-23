using System.Security.Cryptography;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using Microsoft.Extensions.Logging;
namespace BelemRestaurantes.Infrastructure.Ai;

// Shared by Cuia, site search and research jobs. Cache contains evidence, never model answers.
public sealed class ArchivedWebSearch(CompositeWebSearch network, ISearchArchive archive,IExternalContent content,ILogger<ArchivedWebSearch> logger,DraelOptions? drael=null,TavilyOptions? tavily=null,BraveSearchOptions? brave=null,PublicSiteSearchOptions? regional=null):IWebSearch
{
    public async Task<WebSearchResult> SearchAsync(string query,CancellationToken ct)
    {
        query=query.Trim();if(query.Length<2||query.Length>500||SearchTopics.IsPrivate(query))return new("empty",[]);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("search-v6:"+(regional?.Enabled==true?"regional:":"")+(drael?.Ready==true&&drael.SearchEnabled?"drael:":"")+(tavily?.Enabled==true&&tavily.FreeTierConfirmed&&!string.IsNullOrWhiteSpace(tavily.ApiKey)?"tavily:":"")+(brave?.Enabled==true&&brave.FreeTierConfirmed&&!string.IsNullOrWhiteSpace(brave.ApiKey)?"brave:":"")+SearchTopics.Normalize(query))));
        WebSearchResult? result=null;
        try { result=await archive.GetAsync(key,ct); }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){logger.LogWarning("Cache de pesquisa indisponível: {Type}",ex.GetType().Name);}
        if(result is null)
        {
            result=await network.SearchAsync(query,ct);
            try{await archive.SaveAsync(key,result,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex){logger.LogWarning("Persistência da pesquisa indisponível: {Type}",ex.GetType().Name);}
        }
        else result=result with{Status=result.Sources.Count>0?"cached":result.Status};
        var suppressed=await content.SuppressedUrlsAsync(result.Sources.Select(s=>s.Url),ct);
        return result with{Sources=result.Sources.Where(s=>!suppressed.Contains(ExternalContentRules.CanonicalUrl(s.Url)??s.Url)).Take(16).ToArray()};
    }
}
