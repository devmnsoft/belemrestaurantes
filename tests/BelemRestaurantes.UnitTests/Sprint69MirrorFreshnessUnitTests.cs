using System;
using BelemRestaurantes.Application.Abstractions;
using Xunit;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 69 — veredito conservador sobre o frescor persistido dos espelhos Overpass: mesma regra da
/// fonte (idade ausente ou mais de 14 dias = dado antigo; exatamente 14 dias ainda vale).</summary>
public sealed class Sprint69MirrorFreshnessUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static MirrorFreshnessSnapshot Snap(DateTimeOffset? baseUtc) =>
        new("https://espelho.test/api/interpreter", baseUtc, Now);

    [Fact]
    public void FreshBase_IsNotOld()
        => Assert.False(Snap(Now.AddDays(-1)).IsDataOld(Now));

    [Fact]
    public void FourteenDayBoundary_IsStillFresh()
        => Assert.False(Snap(Now.AddDays(-14)).IsDataOld(Now)); // teto excedido só com MAIS que 14 dias

    [Fact]
    public void BeyondFourteenDays_IsOld()
        => Assert.True(Snap(Now.AddDays(-14).AddSeconds(-1)).IsDataOld(Now));

    [Fact]
    public void UnreadableAge_IsConservativelyOld()
        => Assert.True(Snap(null).IsDataOld(Now));

    [Fact]
    public void CustomLimitIsRespected()
    {
        var snap = Snap(Now.AddDays(-3));
        Assert.False(snap.IsDataOld(Now));    // padrão de 14 dias: fresco
        Assert.True(snap.IsDataOld(Now, 2));  // teto mais estrito: 3 dias já é antigo
    }
}
