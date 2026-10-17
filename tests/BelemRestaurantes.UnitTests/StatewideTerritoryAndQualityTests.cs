using System;
using System.Linq;
using BelemRestaurantes.Domain.Data;
using BelemRestaurantes.Domain.Rules;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class StatewideTerritoryAndQualityTests
{
    [Fact]
    public void OfficialParaMunicipalities_CountMustBeExactly144()
    {
        // FASE 2: O Pará possui exatamente 144 municípios oficiais reconhecidos pelo IBGE
        Assert.Equal(144, ParaOfficialCatalog.TotalOfficialMunicipalities);
        Assert.Equal(144, ParaOfficialCatalog.Municipalities.Count);
    }

    [Fact]
    public void OfficialParaMunicipalities_AllIbgeCodesMustBeUnique()
    {
        // FASE 2: Identificar municípios duplicados por código IBGE
        var distinctCodes = ParaOfficialCatalog.Municipalities
            .Select(m => m.IbgeCode)
            .Distinct()
            .Count();

        Assert.Equal(144, distinctCodes);
    }

    [Fact]
    public void OfficialParaMunicipalities_AllNamesMustBeUnique()
    {
        // FASE 2: Identificar municípios duplicados por nome
        var distinctNames = ParaOfficialCatalog.Municipalities
            .Select(m => m.NormalizedName)
            .Distinct()
            .Count();

        Assert.Equal(144, distinctNames);
    }

    [Fact]
    public void OfficialParaMunicipalities_AllIbgeCodesMustStartWithParaPrefix15()
    {
        // FASE 2: Código UF do Pará é 15
        foreach (var muni in ParaOfficialCatalog.Municipalities)
        {
            var codeStr = muni.IbgeCode.ToString();
            Assert.Equal(7, codeStr.Length);
            Assert.StartsWith("15", codeStr);
        }
    }

    [Fact]
    public void OfficialParaMunicipalities_AllIbgeCodesPassModulo10Checksum()
    {
        // FASE 2: Validação matemática do dígito verificador módulo 10 em 100% dos municípios
        foreach (var muni in ParaOfficialCatalog.Municipalities)
        {
            var isValid = ParaOfficialCatalog.ValidateIbgeChecksum(muni.IbgeCode);
            Assert.True(isValid, $"Código IBGE inválido para o município {muni.Name} ({muni.IbgeCode}).");
        }
    }

    [Theory]
    [InlineData("combu")]
    [InlineData("mosqueiro")]
    [InlineData("icoaraci")]
    [InlineData("alter do chao")]
    [InlineData("algodoal")]
    [InlineData("cotijuba")]
    [InlineData("joanes")]
    public void DistrictsAndIslands_AreNotConfusedWithMunicipalities(string localitySlug)
    {
        // FASE 2: Distinguir município, distrito, ilha e localidade turística
        // Localidades como Combu, Mosqueiro, Icoaraci, Alter do Chão NUNCA devem constar como municípios autônomos
        var isRegisteredAsMunicipality = ParaOfficialCatalog.Municipalities
            .Any(m => m.NormalizedName == localitySlug);

        Assert.False(isRegisteredAsMunicipality, $"A localidade/distrito '{localitySlug}' não pode ser classificada como município autônomo.");
    }

    [Theory]
    [InlineData(1501409u)] // Dígito verificador errado (Belém correto é 1501402)
    [InlineData(1302603u)] // Município de Manaus/AM (UF 13, não Pará 15)
    [InlineData(3550308u)] // Município de São Paulo/SP (UF 35)
    [InlineData(1500000u)] // Código inválido
    [InlineData(15123u)]    // Tamanho inválido (< 7 dígitos)
    public void IbgeValidator_RejectsInvalidOrTamperedCodes(uint invalidCode)
    {
        // FASE 2: Teste negativo de rejeição de códigos IBGE adulterados ou de fora do Pará
        var isValid = ParaOfficialCatalog.ValidateIbgeChecksum(invalidCode);
        Assert.False(isValid, $"O código {invalidCode} deveria ter sido rejeitado.");
    }

    [Fact]
    public void RestaurantCompletenessRules_EvaluatesCompleteProfileAccurately()
    {
        // FASE 4: Indicadores de completude do perfil (100%)
        var report = RestaurantCompletenessRules.Evaluate(
            name: "Remanso do Bosque",
            address: "Travessa Perebebuí, 1560, Marco, Belém - PA",
            latitude: -1.433215,
            longitude: -48.468214,
            category: "Culinária Paraense",
            phone: "(91) 3347-2829",
            whatsapp: "(91) 98111-2233",
            openingHours: "Terça a Domingo das 11:30 às 23:00",
            photoUrl: "https://belemrestaurantes.com.br/photos/remanso.webp",
            instagram: "https://instagram.com/remansodobosque",
            facebook: "https://facebook.com/remansodobosque",
            website: "https://remansodobosque.com.br");

        Assert.Equal(100, report.ScorePercent);
        Assert.True(report.HasName);
        Assert.True(report.HasAddress);
        Assert.True(report.HasCoordinates);
        Assert.True(report.HasCategory);
        Assert.True(report.HasPhoneOrContact);
        Assert.True(report.HasOpeningHours);
        Assert.True(report.HasVerifiedPhoto);
        Assert.True(report.HasSocialOrWeb);
        Assert.Empty(report.MissingFields);
        Assert.False(report.NeedsUrgentEnrichment);
    }

    [Fact]
    public void RestaurantCompletenessRules_IdentifiesIncompleteProfileAndUrgentNeeds()
    {
        // FASE 4: Priorizar estabelecimentos sem endereço, sem telefone ou sem foto
        var report = RestaurantCompletenessRules.Evaluate(
            name: "Tacacá da Esquina",
            address: null, // Sem endereço
            latitude: null,
            longitude: null,
            category: "Tacacazeira",
            phone: null, // Sem contato
            whatsapp: null,
            openingHours: null,
            photoUrl: null, // Sem foto verificada
            instagram: null,
            facebook: null,
            website: null);

        // Apenas nome (15) + categoria (10) = 25%
        Assert.Equal(25, report.ScorePercent);
        Assert.True(report.NeedsUrgentEnrichment);
        Assert.Contains("Endereço Completo", report.MissingFields);
        Assert.Contains("Telefone ou WhatsApp", report.MissingFields);
        Assert.Contains("Fotografia Verificada de Fachada ou Prato", report.MissingFields);
    }
}
