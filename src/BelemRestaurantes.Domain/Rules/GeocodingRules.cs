using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BelemRestaurantes.Domain.Rules;

/// <summary>Candidato de geocodificação retornado pela fonte externa (Nominatim/OpenStreetMap).
/// Nunca é confiado: toda adoção passa por <see cref="GeocodingRules.Evaluate"/>.</summary>
public sealed record NominatimCandidate(
    double Latitude,
    double Longitude,
    string Type,
    long PlaceId,
    string? Road,
    string? HouseNumber,
    string? City,
    string? State);

/// <summary>Decisão sobre coordenadas externas para uma ficha.
/// <see cref="Reason"/> é estável e vira <c>last_status</c> da fila administrativa.</summary>
public sealed record GeocodingDecision(
    bool Accepted,
    string Reason,
    double? Latitude,
    double? Longitude,
    string? Precision,
    long? PlaceId);

public sealed record StreetAddress(string Street, int? HouseNumber);

/// <summary>Objeto de construção/restaurante encontrado pela Overpass perto do ponto do logradouro.
/// Coordenada vem da geometria (centro, para way/relation); tags <c>addr:*</c> são pistas, não verdade —
/// toda adoção passa por <see cref="GeocodingRules.EvaluateBuildings"/>.</summary>
public sealed record OsmBuildingElement(long OsmId, string Kind, double Latitude, double Longitude, string? StreetTag, string? HouseNumberTag);

/// <summary>Regras rígidas para adotar coordenadas externas. A ficha nunca é localizada por suposição:
/// só entra candidato dentro do Pará, de nível de endereço/edifício, consistente com o logradouro da ficha
/// (e com o número, quando a ficha tem número). Ambiguidade rejeita em vez de escolher.</summary>
public static class GeocodingRules
{
    /// <summary>Tipos OSM considerados ponto do próprio estabelecimento ou edifício.</summary>
    private static readonly HashSet<string> RooftopTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "house", "building", "amenity", "restaurant", "fast_food", "cafe", "bar", "pub",
        "food_court", "supermarket", "shop", "company", "craft", "industrial", "marketplace"
    };

    // Caixa de contorno do Pará com folga: latitude -9,8..-0,6; longitude -60,5..-47,6.
    private const double MinLatitude = -9.8;
    private const double MaxLatitude = -0.6;
    private const double MinLongitude = -60.5;
    private const double MaxLongitude = -47.6;

    /// <summary>Parte do logradouro e número de uma ficha. O número só vale quando é o último token do
    /// logradouro ("Avenida Gentil Bittencourt 1680") ou um trecho numericamente puro ("..., 1680, ...").
    /// "Rua 14 de Agosto" não produz número — o 14 faz parte do nome.</summary>
    public static StreetAddress SplitStreetAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return new("", null);
        var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var street = parts[0];
        var tokens = street.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int? house = tokens.Length > 0 && IsHouseNumber(tokens[^1]) ? int.Parse(tokens[^1], CultureInfo.InvariantCulture) : null;
        if (house is null && parts.Length >= 2 && IsHouseNumber(parts[1])) house = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return new(street, house);
    }

    public static bool IsCoordinateInRange(double latitude, double longitude) =>
        latitude is >= MinLatitude and <= MaxLatitude && longitude is >= MinLongitude and <= MaxLongitude;

    /// <summary>Avalia o conjunto de candidatos. Exatamente um elegível (mesmo objeto OSM duplicado conta
    /// como um) aceita; zero ou vários rejeitam com motivo explícito.</summary>
    public static GeocodingDecision Evaluate(string address, string city, IReadOnlyList<NominatimCandidate> candidates)
    {
        if (candidates.Count == 0) return Rejected(NoCandidates);
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        void Reject(string reason) => reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

        var street = SplitStreetAddress(address);
        var streetKey = StreetKey(street.Street);
        var cityKey = NameNormalizer.Normalize(city);
        var passed = new List<NominatimCandidate>();

        foreach (var candidate in candidates)
        {
            if (!SameName(candidate.State ?? string.Empty, "Pará")) { Reject("outside_state"); continue; }
            var precision = PrecisionForType(candidate.Type);
            if (precision is null) { Reject("unsupported_type"); continue; }
            if (!IsCoordinateInRange(candidate.Latitude, candidate.Longitude)) { Reject("coordinates_out_of_range"); continue; }
            if (!string.IsNullOrEmpty(candidate.Road))
            {
                var roadKey = StreetKey(candidate.Road);
                if (streetKey.Length > 0 && roadKey.Length > 0 && !roadKey.Contains(streetKey, StringComparison.Ordinal) && !streetKey.Contains(roadKey, StringComparison.Ordinal))
                {
                    Reject("street_mismatch");
                    continue;
                }
            }
            if (cityKey.Length > 1 && !string.IsNullOrEmpty(candidate.City) && !SameName(candidate.City, city)) { Reject("city_mismatch"); continue; }
            if (street.HouseNumber is { } expected)
            {
                if (string.IsNullOrWhiteSpace(candidate.HouseNumber)
                    || !int.TryParse(candidate.HouseNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actual)
                    || actual != expected)
                {
                    Reject("house_number_mismatch");
                    continue;
                }
            }
            passed.Add(candidate);
        }

        var distinct = passed.GroupBy(c => c.PlaceId).Select(g => g.First()).ToArray();
        if (distinct.Length == 0)
        {
            var dominant = reasons.OrderByDescending(kv => kv.Value).First().Key;
            return Rejected(dominant);
        }
        if (distinct.Length > 1) return Rejected("ambiguous");

        var winner = distinct[0];
        return new(true, "accepted", winner.Latitude, winner.Longitude, PrecisionForType(winner.Type), winner.PlaceId);
    }

    /// <summary>Motivo estável quando o Nominatim só devolve o logradouro (sem objeto de endereço/edifício):
    /// gatilho do enriquecimento por Overpass (Sprint 62).</summary>
    public const string UnsupportedType = "unsupported_type";

    /// <summary>Motivo estável quando a fonte não devolveu nada para o endereço completo.</summary>
    public const string NoCandidates = "no_candidates";

    /// <summary>Sprint 64 — motivo estável quando nem o endereço completo nem a busca só do logradouro
    /// devolveram candidato nenhum.</summary>
    public const string NoCandidatesStreet = "no_candidates_street";

    /// <summary>Sprint 64 — logradouro para o "segundo olhar" quando o endereço completo não devolveu
    /// candidato: a parte de logradouro de <see cref="SplitStreetAddress"/>, sem o número final quando esse
    /// é o próprio número da ficha (é a presença dele na consulta que costuma afundar a busca).
    /// Nulo quando repetir a busca não mudaria nada (endereço que já é só o logradouro) ou quando o nome
    /// resultante é curto demais para carregar informação (ex.: "SN").</summary>
    public static string? StreetForSecondLook(string? address)
    {
        var form = StreetForm(address);
        if (form.Length < 3) return null;
        return form.Equals(address?.Trim(), StringComparison.Ordinal) ? null : form;
    }

    /// <summary>Logradouro sem o número final, quando esse é o número da ficha. Numerais que fazem parte
    /// do nome do logradouro são preservados ("Rua 14 de Agosto").</summary>
    public static string StreetForm(string? address)
    {
        var sa = SplitStreetAddress(address);
        var street = sa.Street;
        var tokens = street.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (sa.HouseNumber is { } house && tokens.Length > 1 && IsHouseNumber(tokens[^1])
            && int.Parse(tokens[^1], CultureInfo.InvariantCulture) == house)
            street = string.Join(' ', tokens[..^1]).Trim();
        return street;
    }

    /// <summary>Sprint 65 — escopo estável (logradouro, cidade) usado para cacher confirmações de
    /// "logradouro sem candidatos no OSM". Nulo quando não há logradouro/cidade úteis.</summary>
    public static GeocodingStreetScope? StreetScopeForLookup(string? address, string? city)
    {
        var form = StreetForm(address);
        if (form.Length < 3) return null;
        var cityKey = NameNormalizer.Normalize(city ?? string.Empty);
        if (cityKey.Length < 2) return null;
        return new GeocodingStreetScope(StreetKey(form), cityKey);
    }

/// <summary>Sprint 65 — par normalizado (logradouro, cidade) que identifica um escopo de busca.
/// Fichas com números diferentes no mesmo logradouro e cidade caem no mesmo escopo.</summary>
public sealed record GeocodingStreetScope(string StreetKey, string CityKey);

    /// <summary>Nível de precisão registrado no mapa: objeto de edifício/vaga → rooftop; endereço sem objeto
    /// OSM → approximate. Logradouro, bairro ou município não localizam a ficha.</summary>
    public static string? PrecisionForType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        if (RooftopTypes.Contains(type)) return "rooftop";
        return type.Equals("address", StringComparison.OrdinalIgnoreCase) ? "approximate" : null;
    }

    /// <summary>Âncora para a consulta de edifícios: o primeiro candidato não aproveitável (logradouro ou POI
    /// sem tipo de precisão) cujo nome casa com o logradouro da ficha e que está dentro da caixa do Pará.
    /// Vários candidatos no mesmo logradouro não impedem — a ordem de relevância do Nominatim decide.</summary>
    public static NominatimCandidate? FindStreetAnchor(string address, IReadOnlyList<NominatimCandidate> candidates)
    {
        var street = SplitStreetAddress(address);
        var streetKey = StreetKey(street.Street);
        if (streetKey.Length == 0) return null;
        foreach (var candidate in candidates)
        {
            if (PrecisionForType(candidate.Type) is not null) continue; // candidato já aproveitável não ancora busca
            if (string.IsNullOrEmpty(candidate.Road)) continue;
            if (!IsCoordinateInRange(candidate.Latitude, candidate.Longitude)) continue;
            if (!StreetMatches(streetKey, candidate.Road)) continue;
            return candidate;
        }
        return null;
    }

    /// <summary>Avalia construções/restaurantes encontrados pela Overpass ao redor do ponto do logradouro.
    /// Com número na ficha: só aceita objeto com <c>addr:housenumber</c> igual e logradouro compatível — rooftop.
    /// Sem número: um único objeto confirmado pelo logradouro (ou único na vizinhança) — approximate.
    /// Ambiguidade e ausência rejeitam com motivo estável.</summary>
    public static GeocodingDecision EvaluateBuildings(string address, string city, IReadOnlyList<OsmBuildingElement> elements)
    {
        if (elements.Count == 0) return Rejected("overpass_no_match");
        var street = SplitStreetAddress(address);
        var streetKey = StreetKey(street.Street);

        // Duplicatas do mesmo físico (way+relation, geometria repetida) contam como um: agrupa por centro ~1 m.
        var distinct = new List<OsmBuildingElement>();
        foreach (var group in elements.Where(e => IsCoordinateInRange(e.Latitude, e.Longitude))
            .GroupBy(e => (Math.Round(e.Latitude, 5), Math.Round(e.Longitude, 5))))
            distinct.Add(group.First());
        if (distinct.Count == 0) return Rejected("overpass_no_match");

        if (street.HouseNumber is { } expected)
        {
            var confirmed = new List<OsmBuildingElement>();
            foreach (var el in distinct)
            {
                if (el.HouseNumberTag is null
                    || !int.TryParse(el.HouseNumberTag.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var actual)
                    || actual != expected) continue;
                if (streetKey.Length > 0 && !StreetMatches(streetKey, el.StreetTag)) continue;
                confirmed.Add(el);
            }
            if (confirmed.Count == 1) return new(true, "accepted", confirmed[0].Latitude, confirmed[0].Longitude, "rooftop", confirmed[0].OsmId);
            return Rejected(confirmed.Count > 1 ? "overpass_ambiguous" : "overpass_no_match");
        }

        var streetMatched = distinct.Where(el => streetKey.Length > 0 && StreetMatches(streetKey, el.StreetTag)).ToList();
        if (streetMatched.Count == 1)
            return new(true, "accepted", streetMatched[0].Latitude, streetMatched[0].Longitude, "approximate", streetMatched[0].OsmId);
        if (streetMatched.Count > 1) return Rejected("overpass_ambiguous");
        if (distinct.Count == 1
            && (string.IsNullOrWhiteSpace(distinct[0].StreetTag) || streetKey.Length == 0 || StreetMatches(streetKey, distinct[0].StreetTag)))
            return new(true, "accepted", distinct[0].Latitude, distinct[0].Longitude, "approximate", distinct[0].OsmId);
        return Rejected("overpass_no_match");
    }

    /// <summary>Logradouro marcado no OSM é compatível quando a chave contém a chave da ficha (ou o inverso,
    /// como na regra principal). Marca ausente não contradiz.</summary>
    private static bool StreetMatches(string streetKey, string? tagged)
    {
        if (string.IsNullOrWhiteSpace(tagged)) return true;
        var key = StreetKey(tagged);
        return key.Contains(streetKey, StringComparison.Ordinal) || streetKey.Contains(key, StringComparison.Ordinal);
    }

    /// <summary>Palavras-numerais comuns em logradouros brasileiros, normalizadas para o dígito equivalente
    /// para que "Travessa Três de Maio" corresponda ao "Travessa 3 de Maio" do OSM.</summary>
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
    {
        ["um"] = "1", ["dois"] = "2", ["tres"] = "3", ["quatro"] = "4", ["cinco"] = "5",
        ["seis"] = "6", ["sete"] = "7", ["oito"] = "8", ["nove"] = "9", ["dez"] = "10",
        ["onze"] = "11", ["doze"] = "12", ["treze"] = "13", ["quatorze"] = "14", ["quinze"] = "15",
        ["dezesseis"] = "16", ["dezessete"] = "17", ["dezoito"] = "18", ["dezenove"] = "19", ["vinte"] = "20"
    };

    /// <summary>Chave de comparação de logradouro: minúscula, sem acento/pontuação e numerais verbais como dígito.</summary>
    private static string StreetKey(string value)
    {
        var normalized = NameNormalizer.Normalize(value);
        if (normalized.Length == 0) return normalized;
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (NumberWords.TryGetValue(tokens[i], out var digit)) tokens[i] = digit;
        }
        return string.Join(' ', tokens);
    }

    private static bool IsHouseNumber(string token) => Regex.IsMatch(token, @"^\d{1,5}$");

    private static bool SameName(string left, string right) =>
        NameNormalizer.Normalize(left) == NameNormalizer.Normalize(right) && NameNormalizer.Normalize(left).Length > 0;

    private static GeocodingDecision Rejected(string reason) => new(false, reason, null, null, null, null);
}
