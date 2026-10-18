namespace BelemRestaurantes.Web.Models;

public sealed class DiscoveryTargetRunView
{
    public long CycleId { get; set; }
    public string City { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public int Found { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public DateTime RecordedAt { get; set; }
}

public sealed class DiscoveryCycleView
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Found { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
}
