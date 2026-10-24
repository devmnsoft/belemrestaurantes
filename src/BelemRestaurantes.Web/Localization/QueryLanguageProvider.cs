using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Localization;

namespace BelemRestaurantes.Web.Localization;

/// <summary>Sprint 80 — idioma resolúvel por URL. O parâmetro <c>?lang=xx</c> escolhe o idioma da
/// página sem exigir cookie nem clique: é o endereço que os alternates hreflang publicam, então
/// buscadores e links compartilhados chegam à língua prometida. Prioridade (definida na ordem dos
/// provedores): cookie do usuário (escolha persistida) &gt; parâmetro da URL &gt; Accept-Language do
/// navegador &gt; padrão pt-BR. Somente códigos oficialmente suportados são aceitos, sem sensibilidade
/// a maiúsculas; qualquer outro valor é ignorado em silêncio e a página continua no idioma vigente.</summary>
public sealed class QueryLanguageProvider : RequestCultureProvider
{
    public const string QueryKey = "lang";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext context)
    {
        var value = context.Request.Query[QueryKey].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        var code = value.Trim();
        if (!SupportedLanguages.All.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(code));
    }
}
