using BelemRestaurantes.Application.Abstractions;
using System.Text.RegularExpressions;
namespace BelemRestaurantes.Application.Services;
public static class SearchRanking
{
    public static IEnumerable<WebSearchSource> ForKnownPlace(string query,IEnumerable<string?> names,IEnumerable<WebSearchSource> sources)
    {
        var normalized=SearchTopics.Normalize(query);
        var identified=names.Where(n=>!string.IsNullOrWhiteSpace(n)).Select(n=>SearchTopics.Normalize(n!)).Where(n=>normalized.Contains(n)).Distinct().ToArray();
        return sources.Where(s=>s.SourceKind!="encyclopedia"||identified.Length==0||identified.Any(n=>SearchTopics.Normalize(s.Title).Contains(n)));
    }
    public static IReadOnlyList<WebSearchSource> Merge(string query,IEnumerable<WebSearchSource> sources)
    {
        var phrase=SearchTopics.Normalize(query);
        var words=Regex.Matches(phrase,@"[\p{L}\p{N}]+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value).Where(w=>w.Length>2&&!new[]{"para","quero","onde","como","diga","sobre"}.Contains(w)).Distinct().Take(12).ToArray();
        int Score(WebSearchSource s){var title=SearchTopics.Normalize(s.Title);var text=SearchTopics.Normalize(s.Snippet);return(title.Contains(phrase)?30:0)+words.Sum(w=>title.Contains(w)?5:text.Contains(w)?1:0)+(s.SourceKind=="collected_reference"?2:0);}
        return sources.Where(s=>ExternalContentRules.CanonicalUrl(s.Url) is not null)
            .Where(s=>!(CulinaryQuery.Operational(query)&&s.SourceKind=="encyclopedia"))
            .Where(s=>s.SourceKind!="encyclopedia"||CulinaryQuery.Relevant(query,s.Title,""))
            .Where(s=>CulinaryQuery.Core(query).Length==0||CulinaryQuery.Relevant(query,s.Title,s.Snippet))
            .OrderByDescending(Score).DistinctBy(s=>ExternalContentRules.CanonicalUrl(s.Url)).Take(16).ToArray();
    }
}
