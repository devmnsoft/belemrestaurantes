using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Domain.Data;

namespace BelemRestaurantes.Application.Services;

public sealed class AutoPublicationOptions
{
    public bool Enabled {get;set;}
    public int MaxPerCycle {get;set;}=3;
}
public static class AutoPublicationRules
{
    public static string? BlockReason(ExtractedRestaurantData p,CrawlerSource? source,DeduplicationResult dedup)
    {
        if(source?.Status!="active"||source.SourceType!="structured_data")return "A fonte não é um conector estruturado ativo.";
        if(!p.StructuredIdentity||!p.ExplicitCity||!p.DeclaredWebsite||p.ConfidenceScore<.95)return "Identidade estruturada, município explícito ou site declarado ausentes.";
        if(p.HasConflict||dedup.Outcome!=MatchOutcome.New||dedup.RequiresHumanReview)return "Conflito ou possível duplicidade.";
        if(p.Name.Trim().Length is <3 or >160||string.IsNullOrWhiteSpace(p.Address)||p.Address.Length<12)return "Nome ou endereço insuficiente.";
        if(!ParaOfficialCatalog.Municipalities.Any(c=>NameNormalizer.Normalize(c.Name)==NameNormalizer.Normalize(p.City??"")))return "Município fora do catálogo oficial do Pará.";
        if(!Uri.TryCreate(p.SourceUrl,UriKind.Absolute,out var src)||!Uri.TryCreate(p.Website,UriKind.Absolute,out var site)||src.Scheme!="https"||site.Scheme!="https"||!string.IsNullOrEmpty(src.UserInfo)||!string.Equals((src.Host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?src.Host[4..]:src.Host),(site.Host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?site.Host[4..]:site.Host),StringComparison.OrdinalIgnoreCase))return "Fonte e site declarado não correspondem.";
        return null;
    }
}
