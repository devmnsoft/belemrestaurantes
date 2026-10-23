using System.Text.RegularExpressions;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.Application.Services;

public static class CuiaConversation
{
    public static bool IsFollowUp(string text)
    {
        var p=NameNormalizer.RemoveDiacritics(text).ToLowerInvariant().Trim();
        return p.Length<=160 && (Regex.IsMatch(p,@"^(e |e os|e as|como (eu )?(chego|chegar|vou)|qual (o |a )?(endereco|horario|preco)|quais (os |as )?(precos|horarios)|fica onde|quanto custa|tem (cardapio|menu)|o cardapio|e la|mais opcoes|outras opcoes)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)) || p.Contains("nesse restaurante") || p.Contains("neste restaurante") || p.Contains("desse restaurante") || Regex.IsMatch(p,@"^(tem receita|mostre (um )?video|onde (compro|comprar)|quanto (custa|preciso)|quais desses|e um mais)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)));
    }
    public static string? Anchor(string prompt,IReadOnlyList<string> previous)
        => IsFollowUp(prompt)?previous.Reverse().FirstOrDefault(p=>!IsFollowUp(p)):null;
    public static string SearchQuery(string prompt,string? anchor,string? city)
        => string.Join(" ",new[]{anchor,prompt,city,"Pará"}.Where(s=>!string.IsNullOrWhiteSpace(s))).Trim()[..Math.Min(500,string.Join(" ",new[]{anchor,prompt,city,"Pará"}.Where(s=>!string.IsNullOrWhiteSpace(s))).Trim().Length)];
}
