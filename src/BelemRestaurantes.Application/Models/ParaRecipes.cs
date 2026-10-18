using System.Text.RegularExpressions;

namespace BelemRestaurantes.Application.Models;

public sealed class ParaRecipe
{
    public long Id { get; set; }
    public string Slug { get; set; }="";
    public string Title { get; set; }="";
    public string Subtitle { get; set; }="";
    public string Category { get; set; }="Pratos principais";
    public string Course { get; set; }="Almoço";
    public string City { get; set; }="Belém";
    public string Difficulty { get; set; }="Fácil";
    public int Servings { get; set; }=4;
    public int PrepMinutes { get; set; }=15;
    public int CookMinutes { get; set; }=20;
    public int WaitMinutes { get; set; }
    public string? Story { get; set; }
    public string[] Ingredients { get; set; }=[];
    public string[] Steps { get; set; }=[];
    public string? Tips { get; set; }
    public string? Variations { get; set; }
    public string? ServeWith { get; set; }
    public string? Allergens { get; set; }
    public string? SafetyNote { get; set; }
    public string Kind { get; set; }="inspired";
    public string? SourceUrl { get; set; }
    public string? PermissionNote { get; set; }
    public string? VideoUrl { get; set; }
    public string? VideoTitle { get; set; }
    public string Icon { get; set; }="cuia";
    public string Tone { get; set; }="forest";
    public string Status { get; set; }="draft";
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long[] RestaurantIds { get; set; }=[];
    public int TotalMinutes=>PrepMinutes+CookMinutes+WaitMinutes;
    [System.Text.Json.Serialization.JsonIgnore]
    public string VideoPlatform=>!Uri.TryCreate(VideoUrl,UriKind.Absolute,out var uri)?"":uri.Host.Contains("globo.com",StringComparison.Ordinal)?"Globoplay":uri.Host.Contains("instagram",StringComparison.Ordinal)?"Instagram":uri.Host.Contains("tiktok",StringComparison.Ordinal)?"TikTok":"YouTube";
}
public sealed record RecipeFilter(string? Query=null,string? Category=null,string? Course=null,string? City=null,long? RestaurantId=null,bool Quick=false,int Page=1);

public static class RecipeRules
{
    public static readonly string[] Categories=["Pratos principais","Caldos e cuias","Acompanhamentos","Lanches","Sobremesas","Bebidas"];
    public static bool Https(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo==""&&value.Length<=350;
    public static void Validate(ParaRecipe r)
    {
        if(r.Id<0||r.Version<0||r.Id>0&&r.Version<1||r.Slug is null||!Regex.IsMatch(r.Slug,@"^[a-z0-9]+(?:-[a-z0-9]+)*$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))||r.Slug.Length>120
            ||string.IsNullOrWhiteSpace(r.Title)||r.Title.Length>160||string.IsNullOrWhiteSpace(r.Subtitle)||r.Subtitle.Length>350
            ||!Categories.Contains(r.Category)||r.Course is not("Almoço" or "Jantar" or "Café e lanche" or "Sobremesa")
            ||r.Difficulty is not("Fácil" or "Intermediária" or "Elaborada")||r.Servings is <1 or >50||r.PrepMinutes is <0 or >1440||r.CookMinutes is <0 or >1440||r.WaitMinutes is <0 or >1440||r.TotalMinutes<1
            ||r.Kind is not("inspired" or "official")||r.Status is not("draft" or "published" or "archived")
            ||r.Tone is not("forest" or "river" or "acai" or "gold" or "urucum")||r.Icon is not("cuia" or "fish" or "leaf" or "plate" or "bread" or "cup" or "icecream")
            ||!BelemRestaurantes.Domain.Data.ParaOfficialCatalog.Municipalities.Any(c=>c.Name==r.City))throw new ArgumentException("Confira título, endereço da página, categoria, cidade, rendimento e tempos da receita.");
        if(r.Ingredients is null||r.Steps is null||r.Ingredients.Length is <1 or >40||r.Steps.Length is <1 or >30
            ||r.Ingredients.Concat(r.Steps).Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>1200)
            ||new[]{r.Story,r.Tips,r.Variations,r.ServeWith,r.Allergens,r.SafetyNote,r.PermissionNote}.Any(s=>s?.Length>4000))throw new ArgumentException("Preencha ingredientes e passos; reduza textos acima dos limites.");
        if(r.SourceUrl is not null&&!Https(r.SourceUrl)||r.Kind=="official"&&(!Https(r.SourceUrl)||string.IsNullOrWhiteSpace(r.PermissionNote)))
            throw new ArgumentException("Receita oficial exige fonte HTTPS e registro da autorização ou publicação oficial.");
        if(r.VideoUrl is not null&&(!Https(r.VideoUrl)||!new[]{"youtube.com","www.youtube.com","youtu.be","www.instagram.com","instagram.com","www.tiktok.com","tiktok.com","globoplay.globo.com"}.Contains(new Uri(r.VideoUrl).Host)||string.IsNullOrWhiteSpace(r.VideoTitle)||r.VideoTitle.Length>160))
            throw new ArgumentException("Use título e link público HTTPS de YouTube, Instagram, TikTok ou Globoplay.");
        if(r.RestaurantIds is null||r.RestaurantIds.Length>12||r.RestaurantIds.Any(id=>id<1))throw new ArgumentException("Selecione até 12 estabelecimentos publicados.");
    }
}
