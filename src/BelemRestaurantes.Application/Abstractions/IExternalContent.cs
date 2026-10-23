namespace BelemRestaurantes.Application.Abstractions;

public sealed record ExternalContentFacts(string? PlaceName=null,string? Address=null,IReadOnlyList<string>? MenuItems=null,IReadOnlyList<string>? OpeningHours=null,string? Description=null);
public sealed class ExternalContent
{
    public string FactsJson{get;set;}="{}";
    public ExternalContentFacts Facts{get{try{return System.Text.Json.JsonSerializer.Deserialize<ExternalContentFacts>(FactsJson)??new();}catch(System.Text.Json.JsonException){return new();}}}
    public long Id{get;set;}
    public string Title{get;set;}="";
    public string Url{get;set;}="";
    public string Publisher{get;set;}="";
    public string City{get;set;}="";
    public string Summary{get;set;}="";
    public string Kind{get;set;}="article";
    public string? VideoUrl{get;set;}
    public DateTime? PublishedAt{get;set;}
    public DateTime ObservedAt{get;set;}
    public string State{get;set;}="published";
}
public interface IExternalContent
{
    Task<IReadOnlySet<string>> SuppressedUrlsAsync(IEnumerable<string> urls,CancellationToken ct);
    Task<long?> ObserveAsync(ExternalContent item,CancellationToken ct);
    Task ObservePageAsync(string html,string url,string city,CancellationToken ct);
    Task<IReadOnlyList<ExternalContent>> ListAsync(string? query,string? city,int limit,CancellationToken ct,bool includeSuspended=false);
    Task<ExternalContent?> GetAsync(long id,CancellationToken ct);
    Task<bool> SetStateAsync(long id,string state,string actor,CancellationToken ct);
}
