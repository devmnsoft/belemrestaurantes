namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Limpeza e validação de texto vindo de fontes externas. Tudo que a IA ou a
/// API devolve passa por aqui antes de tocar o banco.
/// </summary>
public static class AddressRules
{
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Remove sequências de espaço e colapsa vírgulas duplicadas.
        var collapsed = System.Text.RegularExpressions.Regex
            .Replace(raw.Trim(), @"\s+", " ")
            .Replace(" ,", ",")
            .Replace(",,", ",");

        return collapsed.Length > 350 ? null : collapsed;
    }
}

public static class NeighborhoodRules
{
    /// <summary>Bairros reconhecidos do núcleo de Belém, para a busca por bairro.</summary>
    public static readonly IReadOnlyList<string> KnownBelemNeighborhoods =
    [
        "Nazaré", "Saúde", "Boa Vista", "Huní", "Umaré", "Reduto", "Jardim Salvador",
        "Ponta da Pedreira", "Batista Campos", "São Lourenço", "Mendes Vikings",
        "Água Branca", "Ipiranga", "Guajará", "Marco", "Montese"
    ];

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim();
        // Recusa entradas longas demais para serem um bairro real (sinal de campo sujo).
        if (cleaned.Length > 120 || !cleaned.Any(char.IsLetter))
        {
            return null;
        }

        return NameNormalizer.ToDisplayCase(cleaned);
    }

    }

public static class CategoryRules
{
    /// <summary>Termos que indicam tentativa de injeção de prompt ou comando.</summary>
    private static readonly string[] PromptInjectionMarkers =
    [
        "ignore", "system", "you are", "apague", "delete", "execute", "drop ", "select ", "insert ",
        "envie", "override", "jailbreak", "exfil"
    ];

    private static readonly Dictionary<string, string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["restaurante"] = "Restaurante",
        ["restaurantes"] = "Restaurante",
        ["pizzaria"] = "Pizzaria",
        ["pizza"] = "Pizzaria",
        ["lanchonete"] = "Lanchonete",
        ["lanches"] = "Lanchonete",
        ["acai"] = "Açaí",
        ["açaí"] = "Açaí",
        ["acaí"] = "Açaí",
        ["cafe"] = "Cafeteria",
        ["café"] = "Cafeteria",
        ["cafeteria"] = "Cafeteria",
        ["tacaca"] = "Tacacá",
        ["tacacá"] = "Tacacá",
        ["churrascaria"] = "Churrascaria",
        ["bar"] = "Bar",
        ["buffet"] = "Buffet",
        ["padaria"] = "Padaria",
        ["hamburgueria"] = "Hamburgueria",
        ["pastelaria"] = "Pastelaria",
        ["açaí e lanches"] = "Açaí",
        ["feira"] = "Feira e mercado",
        ["feira livre"] = "Feira e mercado",
        ["mercado"] = "Feira e mercado",
        ["feira e mercado"] = "Feira e mercado",
        ["açaiteria"] = "Açaí",
        ["sorveteria"] = "Sorveteria",
        ["confeitaria"] = "Confeitaria",
        ["hortifruti"] = "Hortifruti",
        ["peixaria"] = "Peixaria"
    };

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var key = raw.Trim();
        var lowered = key.ToLowerInvariant();

        // Proteção: se a saída da IA (ou qualquer fonte) contém tentativa de
        // injeção de prompt, descartamos em vez de servir como categoria.
        if (PromptInjectionMarkers.Any(m => lowered.Contains(m, StringComparison.Ordinal)) ||
            key.Length > 60)
        {
            return null;
        }

        if (Canonical.TryGetValue(key, out var canonical))
        {
            return canonical;
        }

        // Linha do Google costuma chegar como "Restaurant" ou "Restaurant; Bakery".
        var first = key.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (first is not null && Canonical.TryGetValue(first, out var fromList))
        {
            return fromList;
        }

        if (first is not null && first.Length <= 100 && first.Any(char.IsLetter))
        {
            return NameNormalizer.ToDisplayCase(first);
        }

        return null;
    }
}

public static class SocialLinkRules
{
    /// <summary>
    /// Só <c>https</c> é aceito. <c>http</c>, <c>javascript:</c> e esquemas
    /// estranhos são recusados — é defesa contra XSS armazenado vindo de fonte externa.
    /// </summary>
    public static bool TryValidate(string? url, out string safeUrl)
    {
        safeUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 350)
        {
            return false;
        }

        var candidate = url.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host) || uri.Host.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        safeUrl = uri.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// Confere se a URL do perfil pertence à rede esperada. Impede que um
    /// conector da Meta "aponte" para um domínio de terceiro.
    /// </summary>
    public static bool MatchesHost(string url, string expectedHostSuffix)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var expected = expectedHostSuffix.TrimStart('.').ToLowerInvariant();
        return host.Equals(expected, StringComparison.Ordinal) || host.EndsWith('.' + expected, StringComparison.Ordinal);
    }
}
