using System.Security;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
namespace BelemRestaurantes.Web.Formatting;
public static class ReferencePreview
{
    public static string Render(ExternalContent item)
    {
        static string E(string value)=>SecurityElement.Escape(string.Concat(value.Where(c=>!char.IsControl(c))))??"";
        var words=item.Title.Split(' ',StringSplitOptions.RemoveEmptyEntries);var lines=new List<string>();var line="";
        foreach(var word in words){if(line.Length+word.Length>34&&line.Length>0){lines.Add(line);line="";}line+=(line.Length>0?" ":"")+word[..Math.Min(34,word.Length)];if(lines.Count>=4)break;}
        if(lines.Count<4&&line.Length>0)lines.Add(line);
        var texts=string.Join("",lines.Select((text,i)=>$"""<text x="64" y="{170+i*58}" font-size="44" font-weight="bold">{E(text)}</text>"""));
        return $"""
          <svg xmlns="http://www.w3.org/2000/svg" width="960" height="540" viewBox="0 0 960 540" role="img" aria-label="Prévia gerada pelo portal">
          <rect width="960" height="540" fill="#fffdf5"/><rect x="0" y="0" width="16" height="540" fill="#143f32"/>
          <g font-family="sans-serif" fill="#143f32"><text x="64" y="70" font-size="26">BELÉM RESTAURANTES · REFERÊNCIAS</text>{texts}
          <text x="64" y="440" font-size="25">{E(item.City)} · {E(item.Publisher[..Math.Min(50,item.Publisher.Length)])}</text>
          <text x="64" y="485" font-size="22">Prévia do portal · consulte a matéria ou vídeo original ↗</text></g></svg>
          """;
    }
}
