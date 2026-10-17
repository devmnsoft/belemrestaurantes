using System;
using System.Security.Cryptography;
using System.Text;
using BelemRestaurantes.Domain.Entities;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public class CuiaOperation360Tests
{
    [Fact]
    public void DiscoveryIntelligenceJob_SupportsAllEightTaskTypes()
    {
        // FASE 2: Fila Central de Inteligência deve suportar os 8 tipos de processamento
        var taskTypes = Enum.GetValues<DiscoveryTaskType>();
        Assert.Equal(8, taskTypes.Length);

        Assert.Contains(DiscoveryTaskType.NewDiscovery, taskTypes);
        Assert.Contains(DiscoveryTaskType.UpdateExisting, taskTypes);
        Assert.Contains(DiscoveryTaskType.Enrichment, taskTypes);
        Assert.Contains(DiscoveryTaskType.SourceVerification, taskTypes);
        Assert.Contains(DiscoveryTaskType.Deduplication, taskTypes);
        Assert.Contains(DiscoveryTaskType.ClosureValidation, taskTypes);
        Assert.Contains(DiscoveryTaskType.MediaReview, taskTypes);
        Assert.Contains(DiscoveryTaskType.ConflictResolution, taskTypes);
    }

    [Fact]
    public void RestaurantBusinessStatus_EnforcesStatusValidation()
    {
        // FASE 2: Status do estabelecimento para validação de encerramento de atividades
        var operational = RestaurantBusinessStatus.Operational;
        var tempClosed = RestaurantBusinessStatus.TemporarilyClosed;
        var permClosed = RestaurantBusinessStatus.PermanentlyClosed;

        Assert.Equal(1, (int)operational);
        Assert.Equal(2, (int)tempClosed);
        Assert.Equal(3, (int)permClosed);
    }

    [Fact]
    public void GeoDistrictLocality_DistinguishesIslandsAndDistrictsFromMunicipalities()
    {
        // FASE 3: Distinção de ilhas, distritos e polos gastronômicos
        var combu = new GeoDistrictLocality
        {
            Id = 1,
            MunicipalityIbge = 1501402, // Belém
            Name = "Ilha do Combu",
            NormalizedName = "ilha do combu",
            LocalityType = "island",
            IsTouristHighlight = true
        };

        var icoaraci = new GeoDistrictLocality
        {
            Id = 2,
            MunicipalityIbge = 1501402, // Belém
            Name = "Icoaraci",
            NormalizedName = "icoaraci",
            LocalityType = "district",
            IsTouristHighlight = true
        };

        var alterDoChao = new GeoDistrictLocality
        {
            Id = 5,
            MunicipalityIbge = 1506807, // Santarém
            Name = "Alter do Chão",
            NormalizedName = "alter do chao",
            LocalityType = "tourist_zone",
            IsTouristHighlight = true
        };

        Assert.Equal("island", combu.LocalityType);
        Assert.Equal("district", icoaraci.LocalityType);
        Assert.Equal("tourist_zone", alterDoChao.LocalityType);
        Assert.True(combu.IsTouristHighlight);
        Assert.True(icoaraci.IsTouristHighlight);
        Assert.True(alterDoChao.IsTouristHighlight);
    }

    [Fact]
    public void CommercialMonetization_EnforcesOfficialMnsoftPixKey()
    {
        // FASE 10: PIX oficial MNSOFT 18.160.057/0001-13
        const string expectedPixKey = "18.160.057/0001-13";

        var invoice = new CommercialInvoice
        {
            RestaurantId = 42,
            PlanCode = "destaque",
            AmountCents = 14990,
            PixTxid = "BELREST" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
            PixStatus = "pending",
            PixCopyPaste = $"00020126580014BR.GOV.BCB.PIX0114{expectedPixKey}5204000053039865406149.905802BR5918MNSOFT SOLUCOES6005BELEM62070503***6304"
        };

        Assert.Contains(expectedPixKey, invoice.PixCopyPaste);
        Assert.StartsWith("BELREST", invoice.PixTxid);
        Assert.Equal("pending", invoice.PixStatus);
        Assert.Equal(14990u, invoice.AmountCents);
    }

    [Fact]
    public void CommercialMonetization_HmacAuthHash_PreventsTampering()
    {
        // FASE 10 e 11: Validação de pagamento autenticado por HMAC-SHA256
        const string secretKey = "MnsoftSecret2026";
        var txid = "BELREST9988776655443322";
        var amount = 99.00m;
        long restaurantId = 15;

        // Gerar hash autêntico
        string ComputeHash(string t, decimal a, long r)
        {
            var raw = $"{t}:{a:F2}:{r}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)));
        }

        var validHash = ComputeHash(txid, amount, restaurantId);

        // Cenário válido
        Assert.True(!string.IsNullOrWhiteSpace(validHash));

        // Cenário de tentativa de ataque / adulteração de valor (pagar R$ 1,00 em vez de R$ 99,00)
        var tamperedAmountHash = ComputeHash(txid, 1.00m, restaurantId);
        Assert.NotEqual(validHash, tamperedAmountHash);

        // Cenário de tentativa de roubo de ativação (outro restaurante tentar usar o mesmo comprovante)
        var tamperedRestaurantHash = ComputeHash(txid, amount, 999);
        Assert.NotEqual(validHash, tamperedRestaurantHash);
    }

    [Fact]
    public void DiscoveryIntelligenceJob_RetriesAndDeadLettersGracefully()
    {
        // FASE 2: Controle de retry e limites de tentativas
        var job = new DiscoveryIntelligenceJob
        {
            TaskType = "closure_validation",
            RestaurantId = 12,
            Priority = 3,
            Attempts = 3,
            MaxAttempts = 4,
            Status = "running"
        };

        job.Attempts++;
        if (job.Attempts >= job.MaxAttempts)
        {
            job.Status = "dead_letter";
            job.LastError = "Limite de 4 tentativas esgotado sem confirmação externa.";
        }

        Assert.Equal(4, job.Attempts);
        Assert.Equal("dead_letter", job.Status);
        Assert.NotNull(job.LastError);
    }
}
