using System.Text.Json;
using System.Text.RegularExpressions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Data;

namespace BelemRestaurantes.Infrastructure.OpenData;

public sealed record FoodRegistryBatch(DateTimeOffset FetchedAt, IReadOnlyList<DiscoveredPlace> Places, int ReadCount);

/// <summary>Projeção de dados empresariais públicos. Não materializa sócios, CPF, e-mail ou razão social.</summary>
public static class FoodRegistryParser
{
    public static FoodRegistryBatch ParsePage(string payload)
    {
        using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        if (!root.TryGetProperty("fetchedAt", out var fetched) || fetched.ValueKind != JsonValueKind.String || !fetched.TryGetDateTimeOffset(out var fetchedAt)
            || fetchedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new InvalidOperationException("Data de coleta inválida.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 1024)
            throw new InvalidOperationException("Página cadastral inválida ou acima de 1.024 registros.");
        var places = new List<DiscoveredPlace>();
        foreach (var item in data.EnumerateArray())
        {
            string? Text(string field) => item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            int? Number(string field) => item.TryGetProperty(field,out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
            var cnpj = Text("cnpj");
            if (Text("uf") != "PA" || Number("situacao_cadastral") != 2 || !IsValidCnpj(cnpj)) continue;
            var name = Text("nome_fantasia");
            // Sem nome comercial não criamos restaurante a partir do nome civil de um empresário.
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || !name.Any(char.IsLetter)
                || Regex.IsMatch(name,@"\d{11}",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))
                || NameNormalizer.Normalize(name) is "sem nome fantasia" or "nao informado" or "") continue;
            var code = Number("codigo_municipio_ibge");
            var city = code.HasValue
                ? ParaOfficialCatalog.Municipalities.FirstOrDefault(m => m.IbgeCode == (uint)code.Value)
                : ParaOfficialCatalog.Municipalities.FirstOrDefault(m => m.NormalizedName == NameNormalizer.Normalize(Text("municipio")));
            if (city is null || (Text("municipio") is { Length: > 0 } cityName && NameNormalizer.Normalize(cityName) != city.NormalizedName)) continue;
            var cnae = Number("cnae_fiscal");
            var category = cnae switch
            {
                5611201 => "Restaurante", 5611203 => "Lanchonete", 1091102 or 4721102 => "Padaria",
                5611204 or 5611205 => "Bar", 5612100 => "Comida de rua", 5620104 => "Comida pronta", 4721104 => "Confeitaria",
                _ => null
            };
            if (category is null) continue;
            var plain = NameNormalizer.RemoveDiacritics(name).ToLowerInvariant();
            bool Named(string pattern) => Regex.IsMatch(plain,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if (category is "Restaurante" or "Lanchonete")
                category = Named(@"\b(pizzaria|pizza)\b") ? "Pizzaria"
                    : Named(@"\b(acai|acaiteria|acailandia)\b") ? "Açaí"
                    : Named(@"\btacaca\b") ? "Tacacá"
                    : Named(@"\bhamburgueria\b") ? "Hamburgueria"
                    : Named(@"\b(cafe|cafeteria)\b") ? "Cafeteria" : category;
            var street = string.Join(' ',new[]{Text("descricao_tipo_de_logradouro"),Text("logradouro")}.Where(s=>!string.IsNullOrWhiteSpace(s)));
            var address = string.IsNullOrWhiteSpace(Text("logradouro")) ? null
                : string.Join(", ",new[]{street,Text("numero")}.Where(s=>!string.IsNullOrWhiteSpace(s)));
            var phone = Text("ddd_telefone_1");
            if (phone is not null && new string(phone.Where(char.IsDigit).ToArray()).Length is not (10 or 11)) phone=null;
            places.Add(new("cnpj:"+cnpj,name,NeighborhoodRules.Clean(Text("bairro")),city.Name,category,address,null,null,
                "https://minhareceita.org/"+cnpj,null,null,null,["cnpj",cnae!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)])
            { Phone=phone, State="PA" });
        }
        return new(fetchedAt,places.DistinctBy(p=>p.ExternalId).ToArray(),data.GetArrayLength());
    }

    public static bool IsValidCnpj(string? cnpj)
    {
        if(cnpj is null || cnpj.Length!=14 || cnpj.Any(ch=>ch is < '0' or > '9') || cnpj.Distinct().Count()==1) return false;
        for(var length=12;length<=13;length++)
        {
            var weight=length-7;var sum=0;
            for(var i=0;i<length;i++){sum+=(cnpj[i]-'0')*weight;if(--weight==1)weight=9;}
            var digit=sum%11<2?0:11-sum%11;
            if(cnpj[length]-'0'!=digit)return false;
        }
        return true;
    }
}
