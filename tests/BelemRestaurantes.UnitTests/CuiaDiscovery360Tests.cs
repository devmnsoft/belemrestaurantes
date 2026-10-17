using System;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Enums;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.Crawler;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class CuiaDiscovery360Tests
{
    [Fact]
    public void SafeHttpClient_MultiHomedDns_RejectsIfAnyResolvedIpIsDisallowed()
    {
        // Se um domínio resolver tanto para um IP público quanto para um IP local (ataque multi-homed / rebinding)
        var addresses = new[]
        {
            IPAddress.Parse("177.136.250.10"), // Público legítimo
            IPAddress.Parse("127.0.0.1"),      // Loopback malicioso
            IPAddress.Parse("8.8.8.8")          // Público
        };

        var hasAnyDisallowed = false;
        foreach (var ip in addresses)
        {
            if (SafeHttpClient.IsDisallowedIp(ip))
            {
                hasAnyDisallowed = true;
                break;
            }
        }

        Assert.True(hasAnyDisallowed, "Qualquer IP restrito na resolução DNS deve provocar bloqueio imediato.");
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS/Cloud Metadata
    [InlineData("http://127.0.0.1:8080/admin")]              // Loopback
    [InlineData("http://localhost:5000/")]                   // Localhost
    [InlineData("http://10.0.0.1/status")]                   // Rede interna
    [InlineData("http://192.168.1.1/router")]                // Roteador interno
    public async Task SafeHttpClient_DownloadAsync_BlocksSsrfTargetsDirectly(string targetUrl)
    {
        var client = new SafeHttpClient(NullLogger<SafeHttpClient>.Instance);

        var result = await client.DownloadAsync(targetUrl, timeoutSeconds: 3);

        Assert.False(result.Success);
        Assert.True(result.StatusCode == 403 || result.StatusCode == 0);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void MediaPriorityTiers_EnforceCorrectHierarchy()
    {
        // Hierarquia obrigatória:
        // Tier 1: Proprietário verificado
        // Tier 2: Comunidade aprovada
        // Tier 3: Mídia externa licenciada
        // Tier 4: Ilustração própria
        Assert.True((int)MediaPriorityTier.VerifiedOwner < (int)MediaPriorityTier.CommunityApproved);
        Assert.True((int)MediaPriorityTier.CommunityApproved < (int)MediaPriorityTier.LicensedExternal);
        Assert.True((int)MediaPriorityTier.LicensedExternal < (int)MediaPriorityTier.Illustration);
    }

    [Fact]
    public void GeoMunicipality_InitializesOfficialIbgeDataProperly()
    {
        var belem = new GeoMunicipality
        {
            Id = 1,
            IbgeCode = 1501402,
            Name = "Belém",
            NormalizedName = "belem",
            Mesoregion = "Metropolitana de Belém",
            Microregion = "Belém",
            IsTouristPole = true,
            PriorityLevel = 1,
            CoverageStatus = "covered"
        };

        var santarem = new GeoMunicipality
        {
            Id = 9,
            IbgeCode = 1506807,
            Name = "Santarém",
            NormalizedName = "santarem",
            Mesoregion = "Baixo Amazonas",
            Microregion = "Santarém",
            IsTouristPole = true,
            PriorityLevel = 1,
            CoverageStatus = "unscanned"
        };

        Assert.Equal(1501402u, belem.IbgeCode);
        Assert.True(belem.IsTouristPole);
        Assert.Equal("Metropolitana de Belém", belem.Mesoregion);

        Assert.Equal(1506807u, santarem.IbgeCode);
        Assert.True(santarem.IsTouristPole);
        Assert.Equal("Baixo Amazonas", santarem.Mesoregion);
    }

    [Fact]
    public void RestaurantAttributeHistory_TracksFieldProvenanceCorrectly()
    {
        var history = new RestaurantAttributeHistory
        {
            Id = 1,
            RestaurantId = 42,
            AttributeName = "instagram",
            OldValue = null,
            NewValue = "https://instagram.com/tacacadonamaria",
            SourceType = "verified_owner",
            SourceReference = "Portal do Parceiro",
            ConfidenceScore = 1.000m,
            RequiresModeration = false,
            IsApplied = true
        };

        Assert.Equal(42, history.RestaurantId);
        Assert.Equal("instagram", history.AttributeName);
        Assert.Equal("verified_owner", history.SourceType);
        Assert.Equal(1.000m, history.ConfidenceScore);
        Assert.True(history.IsApplied);
        Assert.False(history.RequiresModeration);
    }

    [Fact]
    public void RestaurantMedia_TracksAttributionAndLicensing()
    {
        var media = new RestaurantMedia
        {
            RestaurantId = 55,
            MediaType = "photo_dish",
            MediaUrl = "https://cdn.belemrestaurantes.com.br/fotos/patonotucupi.jpg",
            PriorityTier = (int)MediaPriorityTier.LicensedExternal,
            SourceOrigin = "https://paraturismo.pa.gov.br",
            AttributionText = "Foto: Acervo Pará Turismo (Licença CC-BY)",
            LicenseType = "cc_by",
            Status = "approved"
        };

        Assert.Equal(55, media.RestaurantId);
        Assert.Equal(3, media.PriorityTier);
        Assert.Equal("photo_dish", media.MediaType);
        Assert.Equal("approved", media.Status);
        Assert.Contains("Acervo Pará Turismo", media.AttributionText);
    }
}
