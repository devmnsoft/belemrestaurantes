namespace BelemRestaurantes.Application.Abstractions;
public sealed record ResearchJob(long Id,string City,string Query);
public sealed class ResearchJobStatus{public long Id{get;set;} public string City{get;set;}="";public string State{get;set;}="";public string? LastStatus{get;set;} public int SourceCount{get;set;}public int QueuedCount{get;set;}public DateTime CreatedAt{get;set;}}
public interface IResearchJobs
{
    Task<IReadOnlyList<ResearchJobStatus>> RecentAsync(CancellationToken ct);
    Task EnqueueAsync(CancellationToken ct);
    Task<ResearchJob?> ClaimAsync(CancellationToken ct,long? targetId=null);
    Task CompleteAsync(long id,string status,int sources,int queued,CancellationToken ct);
}
