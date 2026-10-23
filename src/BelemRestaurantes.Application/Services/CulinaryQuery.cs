using System.Text.RegularExpressions;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.Application.Services;

public static class CulinaryQuery
{
    public static string Correct(string query) => Regex.Replace(query, @"\bcamara[cç][aã]o\b", "camarão", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Noise = ["receita", "receitas", "ingredientes", "ingrediente", "restaurante", "restaurantes", "belem", "para", "mostre", "video", "videos", "publicada", "publicado", "comprar", "compro", "custa", "quanto", "porcoes", "pessoas", "preciso", "publicadas", "publicados", "algum", "alguma", "deles", "ela", "ele", "desses", "desses", "perto", "mais", "barato", "barata", "opcao", "opcoes", "tem", "fazem", "vende", "vendem", "quatro", "hoje", "preparar"];
    public static string[] Core(string query) => SearchLanguage.Terms(Correct(query)).Where(w => !Noise.Contains(w) && !w.All(char.IsDigit)).ToArray();
    public static bool Relevant(string query, string title, string summary)
    {
        var core = Core(query);
        if (core.Length == 0) return false;
        var text = " " + NameNormalizer.Normalize(title + " " + summary) + " ";
        return core.Count(w => Regex.IsMatch(text, @"\b" + Regex.Escape(w) + @"\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) >= Math.Min(core.Length, 2);
    }
    public static bool Operational(string query) => Regex.IsMatch(NameNormalizer.Normalize(query), @"\b(horario|horarios|comprar|compro|preco|precos|custa|cardapio|vende|vendem|restaurante|restaurantes|bar|bares|pub|pubs|receita|receitas|ingredientes|video|videos)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool IsCulinary(string query) => Regex.IsMatch(NameNormalizer.Normalize(Correct(query)), @"camarao|hamburguer|tacaca|tucupi|jambu|manicoba|vatapa|pato|pirarucu|filhote|receita|ingrediente", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static string Topic(string query) => string.Join(' ', Core(query));
    public static string ResearchQuery(string prompt,string topic)
    {
        var text=NameNormalizer.Normalize(prompt);
        if(text.Contains("compr")||text.Contains("custa")||text.Contains("barat"))return topic+" ingredientes comprar mercado Belém PA";
        if(text.Contains("video"))return topic+" receita vídeo autor";
        if(text.Contains("receita")||text.Contains("ingrediente"))return topic+" receita ingredientes autor";
        return topic+" Belém PA restaurante cardápio";
    }
}
