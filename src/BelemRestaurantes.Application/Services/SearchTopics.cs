using System.Text.RegularExpressions;
using BelemRestaurantes.Domain.Rules;
namespace BelemRestaurantes.Application.Services;

// Only a public culinary vocabulary is counted. No raw query, identity, IP or conversation is stored.
public static class SearchTopics
{
    private static readonly string[] Vocabulary = ["tacacá", "açaí", "jambu", "tucupi", "pato", "maniçoba", "peixe", "camarão", "caranguejo", "chocolate", "culinária", "restaurante", "restaurantes", "cardápio", "gastronomia", "ver-o-peso", "combu", "belém", "mosqueiro", "icoaraci", "alter do chão", "santarém", "marajó", "receitas", "travessias", "roteiros"];
    public static string Normalize(string query) => Regex.Replace(NameNormalizer.RemoveDiacritics(query.Trim().ToLowerInvariant()), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool IsPrivate(string query) => query.Contains('@') || Regex.IsMatch(query, @"\d[\d. /()-]{7,}\d|\b(?:senha|password|token|cpf|cnpj|chave)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public static IReadOnlyList<string> Extract(string query)
    {
        if(query.Length>500 || IsPrivate(query)) return [];
        var normalized=Normalize(query);
        return Vocabulary.Where(t=>Regex.IsMatch(normalized,@"(?<![\p{L}\p{N}])"+Regex.Escape(Normalize(t))+@"(?![\p{L}\p{N}])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))).Select(t=>t=="restaurantes"?"restaurante":t).Distinct().Take(5).ToArray();
    }
}
