using BelemRestaurantes.Application.Models;

namespace BelemRestaurantes.Application.Services;

public static class SnapshotDiscoveryPage
{
    public static (PlaceSearchResult Result, DiscoveryPageProgress Progress) Slice(PlaceSearchResult source, int limit, int offset, DateTimeOffset? previousSnapshot)
    {
        limit = Math.Clamp(limit,1,500);
        var ordered=source.Places.OrderBy(p=>p.ExternalId,StringComparer.Ordinal).ToArray();
        if(previousSnapshot is null || Math.Abs((source.QueriedAt-previousSnapshot.Value).TotalMilliseconds)>=1 || offset<0 || offset>=ordered.Length)offset=0;
        var page=ordered.Skip(offset).Take(limit).ToArray();
        var next=offset+page.Length;var more=next<ordered.Length;
        return (source with {Places=page},new(more?next:0,more,source.QueriedAt,ordered.Length));
    }
}
