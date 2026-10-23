using System.Text.RegularExpressions;
using BelemRestaurantes.Domain.Rules;
namespace BelemRestaurantes.Application.Services;
public static class SearchLanguage
{
    private static readonly HashSet<string> Stop=["o","a","os","as","um","uma","e","de","do","da","dos","das","que","qual","quais","quem","era","seria","significa","sao","eh","como","me","diga","fale","explique","sobre","quero","saber","pesquise","pesquisa","procure","encontre","conhecer","por","favor","no","na","em","com","para","site"];
    public static IReadOnlyList<string> Terms(string query)=>Regex.Matches(NameNormalizer.RemoveDiacritics(query.ToLowerInvariant()),@"[\p{L}\p{N}]+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value).Where(w=>w.Length>=3&&!Stop.Contains(w)).Distinct().Take(8).ToArray();
    public static string? Municipality(string query)
    {
        var normalized=" "+NameNormalizer.Normalize(query)+" ";
        return BelemRestaurantes.Domain.Data.ParaOfficialCatalog.Municipalities.OrderByDescending(c=>c.NormalizedName.Length).FirstOrDefault(c=>normalized.Contains(" "+c.NormalizedName+" ",StringComparison.Ordinal))?.Name;
    }
}
