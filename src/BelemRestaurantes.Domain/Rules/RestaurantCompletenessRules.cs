using System;
using System.Collections.Generic;

namespace BelemRestaurantes.Domain.Rules;

public sealed record ProfileCompletenessReport(
    int ScorePercent,
    bool HasName,
    bool HasAddress,
    bool HasCoordinates,
    bool HasCategory,
    bool HasPhoneOrContact,
    bool HasOpeningHours,
    bool HasVerifiedPhoto,
    bool HasSocialOrWeb,
    IReadOnlyList<string> MissingFields,
    bool NeedsUrgentEnrichment);

public static class RestaurantCompletenessRules
{
    public static ProfileCompletenessReport Evaluate(
        string name,
        string? address,
        double? latitude,
        double? longitude,
        string? category,
        string? phone,
        string? whatsapp,
        string? openingHours,
        string? photoUrl,
        string? instagram,
        string? facebook,
        string? website)
    {
        var missing = new List<string>();
        int score = 0;

        // 1. Nome válido: 15%
        bool hasName = !string.IsNullOrWhiteSpace(name) && name.Trim().Length >= 2;
        if (hasName) score += 15;
        else missing.Add("Nome do Estabelecimento");

        // 2. Endereço completo: 20%
        bool hasAddress = !string.IsNullOrWhiteSpace(address) && address.Trim().Length >= 5;
        if (hasAddress) score += 20;
        else missing.Add("Endereço Completo");

        // 3. Coordenadas geográficas: 10%
        bool hasCoords = latitude.HasValue && longitude.HasValue && Math.Abs(latitude.Value) > 0.001;
        if (hasCoords) score += 10;
        else missing.Add("Localização no Mapa (GPS)");

        // 4. Categoria definida: 10%
        bool hasCategory = !string.IsNullOrWhiteSpace(category);
        if (hasCategory) score += 10;
        else missing.Add("Categoria Gastronômica");

        // 5. Telefone ou WhatsApp: 15%
        bool hasContact = !string.IsNullOrWhiteSpace(phone) || !string.IsNullOrWhiteSpace(whatsapp);
        if (hasContact) score += 15;
        else missing.Add("Telefone ou WhatsApp");

        // 6. Horários de funcionamento: 10%
        bool hasHours = !string.IsNullOrWhiteSpace(openingHours);
        if (hasHours) score += 10;
        else missing.Add("Horário de Funcionamento");

        // 7. Fotografia verificada: 10%
        bool hasPhoto = !string.IsNullOrWhiteSpace(photoUrl);
        if (hasPhoto) score += 10;
        else missing.Add("Fotografia Verificada de Fachada ou Prato");

        // 8. Redes sociais / site oficial: 10%
        bool hasSocial = !string.IsNullOrWhiteSpace(instagram) || !string.IsNullOrWhiteSpace(facebook) || !string.IsNullOrWhiteSpace(website);
        if (hasSocial) score += 10;
        else missing.Add("Perfil em Rede Social Oficial ou Website");

        int finalScore = Math.Clamp(score, 0, 100);
        bool needsUrgent = !hasPhoto || !hasAddress || !hasContact;

        return new ProfileCompletenessReport(
            ScorePercent: finalScore,
            HasName: hasName,
            HasAddress: hasAddress,
            HasCoordinates: hasCoords,
            HasCategory: hasCategory,
            HasPhoneOrContact: hasContact,
            HasOpeningHours: hasHours,
            HasVerifiedPhoto: hasPhoto,
            HasSocialOrWeb: hasSocial,
            MissingFields: missing,
            NeedsUrgentEnrichment: needsUrgent);
    }
}
