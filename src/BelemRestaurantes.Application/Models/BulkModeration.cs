namespace BelemRestaurantes.Application.Models;

public sealed record ModerationFilter(string? Term = null, string? City = null, string? Category = null,
    string? Source = null, string? Completeness = null, string? Duplicate = null,
    DateTime? From = null, DateTime? Until = null,string? Stage=null,string? Sort=null);
public sealed class ModerationCandidate
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string? Category { get; set; }
    public string? Address { get; set; }
    public string? Neighborhood { get; set; }
    public string? Phone { get; set; }
    public string? SourceUrl { get; set; }
    public string? ExternalId { get; set; }
    public string? Source { get; set; }
    public string Status { get; set; } = "";
    public string Stage { get; set; } = "pending";
    public DateTime DiscoveredAt { get; set; }
    public bool Duplicate { get; set; }
    public bool ValidCity { get; set; }
    public int Completeness => new[] { Name, City, Category, Address, Phone, SourceUrl }.Count(v => !string.IsNullOrWhiteSpace(v)) * 100 / 6;
    public string? BlockReason => Status != "pending" ? "O cadastro já recebeu uma decisão." : Duplicate ? "Possível duplicidade: reconciliação necessária."
        : !ValidCity || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Category) || string.IsNullOrWhiteSpace(Address)
          || !Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
        ? "Nome, município oficial, categoria, endereço e fonte HTTPS são obrigatórios." : null;
}
public sealed record ModerationDecision(long Id, string Name, string Outcome, string Reason);
public sealed record ModerationBatch(string Id, string Operation, string? Reason, IReadOnlyList<ModerationCandidate> Items,
    IReadOnlyList<ModerationDecision>? Results = null);
