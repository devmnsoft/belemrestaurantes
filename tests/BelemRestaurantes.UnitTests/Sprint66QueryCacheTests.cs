using System.Collections.Generic;
using System.Text.Json;
using BelemRestaurantes.Domain.Rules;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 66 — payload do cache de consulta: o round-trip JSON não pode distorcer nenhum campo
/// do candidato (a avaliação estrita roda de novo sobre esses valores).</summary>
public sealed class Sprint66QueryCacheTests
{
    private static List<NominatimCandidate> SamplePool() =>
    [
        new(-1.4667324, -48.4918527, "house", 99001, "Travessa Do Dito", "1680", "Belém", "Pará"),
        new(-8.5041620, -63.8517530, "road", 456, null, null, null, null),
        new(-1.4700001, -48.5000001, "suburb", 7, "RUA 14 DE AGOSTO", null, "Tucuruí", null)
    ];

    [Fact]
    public void PoolJsonRoundTripPreservesEveryField()
    {
        var original = SamplePool();
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<List<NominatimCandidate>>(json);
        Assert.NotNull(restored);
        Assert.Equal(original, restored); // record de igualdade por valor, item a item
    }

    [Fact]
    public void PoolSerializationIsDeterministic()
    {
        Assert.Equal(JsonSerializer.Serialize(SamplePool()), JsonSerializer.Serialize(SamplePool()));
    }

    [Fact]
    public void EmptyPoolRoundTripsToEmpty()
    {
        IReadOnlyList<NominatimCandidate> empty = [];
        var restored = JsonSerializer.Deserialize<List<NominatimCandidate>>(JsonSerializer.Serialize(empty, new JsonSerializerOptions()));
        Assert.NotNull(restored);
        Assert.Empty(restored);
    }
}
