using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;

namespace BelemRestaurantes.Infrastructure.Crawler;

// Audited public first-party menu. Fail closed when its heading structure changes.
public static class RemansoMenuSource
{
    public const string Url="https://www.restauranteremanso.com.br/cardapio/";
    public static IReadOnlyList<CulinaryReference> Parse(string html,string url,DateTime now)
    {
        if(url!=Url)return [];
        var title=Regex.Match(html,@"<title[^>]*>(.*?)</title>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100));
        if(!ExternalContentRules.Clean(title.Groups[1].Value,250).Contains("Remanso",StringComparison.OrdinalIgnoreCase))return [];
        var result=new List<CulinaryReference>();var enabled=false;
        foreach(Match m in Regex.Matches(html,@"<h([24])\b[^>]*>(.*?)</h\1>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(200)))
        {
            var text=ExternalContentRules.Clean(m.Groups[2].Value,220);
            if(m.Groups[1].Value=="2") {enabled=new[]{"ENTRADAS E PETISCOS","PEIXES E FRUTOS DO MAR","CARNES E AVES","PARA OS VEGETARIANOS","SOBREMESAS"}.Contains(text.ToUpperInvariant());continue;}
            if(!enabled||text.Length<4)continue;
            result.Add(new(0,"dish_evidence",text,url,"Restaurante Remanso do Bosque","Prato listado no cardápio público do Remanso do Bosque. A descrição não revela a receita completa; confirme disponibilidade antes da visita.",[],null,"Restaurante Remanso do Bosque","Belém",null,[],now,null,"Título de prato extraído da seção gastronômica do cardápio oficial. Não é uma receita oficial publicada.",null));
        }
        return result.Take(36).ToArray();
    }
}
