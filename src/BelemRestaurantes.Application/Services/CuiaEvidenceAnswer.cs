using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;

namespace BelemRestaurantes.Application.Services;

public static class CuiaEvidenceAnswer
{
    public static bool HasValidCitations(string answer,int count)
    {
        var matches=Regex.Matches(answer,@"\[(\d+)\]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return matches.Count>0 && matches.All(m=>int.TryParse(m.Groups[1].Value,out var n)&&n>=1&&n<=count);
    }
    public static string Menu(IReadOnlyList<WebSearchSource> sources)
        => "Encontrei estas menções de cardápio nas fontes identificadas:\n\n"+string.Join("\n",sources.SelectMany((s,i)=>s.MenuItems.Take(8).Select(m=>"- **"+Clean(s.PlaceName??s.Title)+"**: "+Clean(m)+" ["+(i+1)+"]")))+"\n\nAs menções disponíveis não confirmam um cardápio oficial vigente nem a disponibilidade de hoje. Confira a data e o canal original antes de ir.";
    public static string Partial(string prompt,IReadOnlyList<WebSearchSource> sources,string? anchor)
    {
        var normalized=BelemRestaurantes.Domain.Rules.NameNormalizer.RemoveDiacritics(prompt).ToLowerInvariant();
        var header=anchor is null?"Localizei referências para continuar sua pesquisa:":"Continuando a pesquisa sobre **"+Clean(anchor)+"**:";
        var rows=sources.Take(8).Select((s,i)=>"- **"+Clean(s.Title)+"** ["+(i+1)+"]"+(string.IsNullOrWhiteSpace(s.Snippet)?"":" · Trecho indexado: "+Clean(string.Join(" ",s.Snippet.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Take(22))))+(s.Address is {} a?" · Endereço informado: "+Clean(a):"")+(s.OpeningHours.Count>0&&normalized.Contains("horar")?" · Horários publicados: "+string.Join("; ",s.OpeningHours.Take(3).Select(Clean)):""));
        return header+"\n\n"+string.Join("\n",rows)+"\n\n"+(normalized.Contains("prec")||normalized.Contains("custa")?"Não há preços atuais verificados nesta consulta. Consulte o cardápio ou contato original.":normalized.Contains("cheg")?"Abra a fonte para confirmar o endereço e use Como chegar. Não há linha, tarifa ou trajeto de transporte confirmado nesta pesquisa.":"As referências ajudam a identificar opções; consulte os links para conferir detalhes e atualidade. Não houve síntese por IA nesta resposta.");
    }
    private static string Clean(string text)=>text.Replace("[","").Replace("]","").Replace("*","").Replace("\n"," ").Replace("\r"," ")[..Math.Min(220,text.Replace("[","").Replace("]","").Replace("*","").Replace("\n"," ").Replace("\r"," ").Length)];
}
