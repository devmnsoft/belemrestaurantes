using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;

namespace BelemRestaurantes.Infrastructure.Crawler;

// Only explicitly declared facts. Instructions and images are not republished.
public static class CulinaryStructuredParser
{
    public static IReadOnlyList<CulinaryReference> Parse(string html, string url, string topic, DateTime now)
    {
        var found = new List<CulinaryReference>();
        foreach (Match script in Regex.Matches(html, """<script\b[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(250)))
        {
            if (script.Groups[1].Length > 262144) continue;
            try
            {
                using var doc = JsonDocument.Parse(script.Groups[1].Value, new JsonDocumentOptions { MaxDepth = 32 });
                foreach (var node in Nodes(doc.RootElement))
                {
                    var type = Text(node, "@type");
                    if(type=="Product")
                    {
                        var product=Clean(Text(node,"name"),220);
                        if(!CulinaryQuery.Relevant(topic,product,""))continue;
                        if(!node.TryGetProperty("offers",out var offer))continue;
                        if(offer.ValueKind==JsonValueKind.Array)offer=offer.EnumerateArray().FirstOrDefault();
                        if(offer.ValueKind!=JsonValueKind.Object)continue;
                        var priceText=offer.TryGetProperty("price",out var priceNode)?priceNode.ToString():"";
                        if(!decimal.TryParse(priceText,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var price)||price<=0||price>100000||Text(offer,"priceCurrency")!="BRL")continue;
                        found.Add(new(0,"ingredient_offer",product,url,new Uri(url).Host,"Preço observado na página do produto. Quantidade, frete, estoque e entrega em Belém devem ser conferidos na loja.",[],null,null,null,null,[],now,null,"Metadados Product/Offer com preço e moeda BRL. Não representa preço médio nem custo total da receita.",null,price,"BRL"));
                        continue;
                    }
                    if (type != "Recipe" && !Strings(node,"@type").Contains("Recipe")) continue;
                    var name = Clean(Text(node, "name"), 220);
                    if (!CulinaryQuery.Relevant(topic, name, "")) continue;
                    var ingredients = Strings(node, "recipeIngredient").Select(s => Clean(s, 250)).Where(s => s.Length > 0).Take(40).ToArray();
                    if (ingredients.Length < 2) continue;
                    var author = Text(node, "author");
                    if (node.TryGetProperty("author", out var a))
                    {
                        if (a.ValueKind == JsonValueKind.Array) a = a.EnumerateArray().FirstOrDefault();
                        if (a.ValueKind == JsonValueKind.Object) author = Text(a, "name");
                    }
                    if (string.IsNullOrWhiteSpace(author)) author = new Uri(url).Host;
                    var video = Nodes(node).Where(n => Text(n, "@type") == "VideoObject")
                        .Select(n => Video(Text(n, "embedUrl")) ?? Video(Text(n, "url")) ?? Video(Text(n, "contentUrl"))).FirstOrDefault(v => v is not null);
                    var published = DateTimeOffset.TryParse(Text(node, "datePublished"), out var date) && date.UtcDateTime <= now.AddDays(1) ? date.UtcDateTime : (DateTime?)null;
                    found.Add(new(0, "recipe_reference", name, url, Clean(author, 180),
                        "Receita publicada por " + Clean(author, 180) + ". Ingredientes identificados na fonte; consulte o preparo original. Não atribuída a restaurante de Belém.",
                        ingredients, video, null, null, null, [], now, published,
                        "Metadados Recipe da página original; autoria não comprova vínculo com restaurante.", Clean(Text(node, "recipeYield"), 120)));
                }
            }
            catch (JsonException) { /* Invalid structured block is not evidence. */ }
        }
        if(found.Count==0)
        {
            var scope=Regex.Match(html,"""<main\b[^>]*itemtype=["']https?://schema.org/Recipe["'][^>]*>(.*?)</main>""",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(250));
            if(scope.Success)
            {
                var body=scope.Groups[1].Value;
                var name=Regex.Match(body,"""<meta\b[^>]*itemprop=["']name["'][^>]*content=["']([^"']+)["']""",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
                var title=Clean(name.Groups[1].Value,220);
                var ingredients=Regex.Matches(body,"""<li\b[^>]*itemprop=["']recipeIngredient["'][^>]*>(.*?)</li>""",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(200)).Select(m=>Clean(m.Groups[1].Value,250)).Where(s=>s.Length>0).Take(40).ToArray();
                if(ingredients.Length>=2&&CulinaryQuery.Relevant(topic,title,""))
                    found.Add(new(0,"recipe_reference",title,url,new Uri(url).Host,"Referência de receita com ingredientes publicados. Leia a autoria e o preparo na fonte; não é atribuída automaticamente a restaurante.",ingredients,null,null,null,null,[],now,null,"Microdados Recipe e recipeIngredient da página original. Vínculo com restaurante não demonstrado.",null));
            }
        }
        return found.DistinctBy(r => r.Url).ToArray();
    }
    public static string? Video(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "") return null;
        if (uri.Host is "www.youtube.com" or "youtube.com" or "youtu.be")
        {
            var m = Regex.Match(value, @"(?:watch\?v=|embed/|youtu\.be/)([a-zA-Z0-9_-]{11})(?:[?&#/]|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (m.Success) return "https://www.youtube.com/watch?v=" + m.Groups[1].Value;
        }
        return null;
    }
    private static string Clean(string text, int limit) => ExternalContentRules.Clean(text, limit);
    private static string Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static IEnumerable<string> Strings(JsonElement node, string name) => node.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!) : [];
    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object) { yield return node; foreach (var p in node.EnumerateObject()) foreach (var child in Nodes(p.Value)) yield return child; }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) foreach (var child in Nodes(item)) yield return child;
    }
}
