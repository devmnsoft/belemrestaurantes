using System.Net;
using System.Text.RegularExpressions;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
namespace BelemRestaurantes.Application.Services;
public static class ExternalContentRules
{
    public static string? CanonicalUrl(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||uri.Port!=443||uri.HostNameType!=UriHostNameType.Dns||!uri.Host.Contains('.')||new[]{".local",".localhost",".internal"}.Any(s=>uri.Host.EndsWith(s,StringComparison.OrdinalIgnoreCase)))return null;
        var b=new UriBuilder(uri){Fragment=""};
        b.Query=string.Join("&",uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(x=>!x.StartsWith("utm_",StringComparison.OrdinalIgnoreCase)&&!x.StartsWith("fbclid=",StringComparison.OrdinalIgnoreCase)&&!x.StartsWith("gclid=",StringComparison.OrdinalIgnoreCase)));
        return b.Uri.AbsoluteUri.Length<=1800?b.Uri.AbsoluteUri:null;
    }
    public static string Clean(string value,int max)=>BelemRestaurantes.Application.Formatting.PublicText.Clean(value,max);
    public static bool Relevant(string title,string city)
    {
        var text=NameNormalizer.RemoveDiacritics(title).ToLowerInvariant();
        var local=ParaOfficialCatalog.Municipalities.Any(c=>NameNormalizer.Normalize(c.Name)==NameNormalizer.Normalize(city));
        return local && Regex.IsMatch(text,@"\b(gastronom\p{L}*|restaurantes?|culinari\p{L}*|comer|comidas?|turis\p{L}*|passeios?|ilhas?|praias?|mercados?|cardapios?|sabores?|tacaca|acai|chefs?|cultura\p{L}*|artesanato|peixes?|jambu|tucupi|cozinha\p{L}*|ver-o-peso)\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    }
    public static bool Regional(string text,string city)
    {
        var folded=NameNormalizer.RemoveDiacritics(Clean(text,50000)).ToLowerInvariant();
        return text.Contains("Pará",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(folded,@"\b(paraense|combu|cotijuba|icoaraci|mosqueiro|"+Regex.Escape(NameNormalizer.RemoveDiacritics(city).ToLowerInvariant())+@")\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    }
    public static string Summary(string city,string publisher,bool video)=>"Referência "+(video?"em vídeo":"externa")+" encontrada durante pesquisa sobre gastronomia, cultura ou passeios em "+city+". Consulte a publicação original de "+publisher+" para os detalhes. Informações de funcionamento e serviços devem ser confirmadas na fonte.";
    public static string? FindVideoLink(string text)
    {
        foreach(Match m in Regex.Matches(WebUtility.HtmlDecode(text[..Math.Min(text.Length,50000)]),"""https://[^\s<>"']+""",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))if(Video(m.Value))return m.Value;
        return null;
    }
    public static bool Video(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||CanonicalUrl(url) is null)return false;
        var h=u.Host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?u.Host[4..]:u.Host;
        return h=="youtu.be"&&u.AbsolutePath.Length>2 || h=="youtube.com"&&(u.AbsolutePath.StartsWith("/watch")&&u.Query.Contains("v=")||u.AbsolutePath.StartsWith("/embed/")||u.AbsolutePath.StartsWith("/shorts/")) || h=="vimeo.com"&&u.AbsolutePath.Skip(1).Any(char.IsDigit) || h=="tiktok.com"&&u.AbsolutePath.Contains("/video/") || h=="instagram.com"&&u.AbsolutePath.StartsWith("/reel/");
    }
}
