using System;
using System.Linq;
using BelemRestaurantes.Application.Abstractions;
using Xunit;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 71 — mescla dos espelhos configurados com as observações persistidas: fonte única do
/// veredito conservador, compartilhada entre o painel /admin/geocodificacao e o CLI --verify-geocoding.</summary>
public sealed class Sprint71MirrorFreshnessViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<string> Configured = new[]
    {
        "https://a.test/api/interpreter",
        "https://a.test/api/interpreter/", // duplicado após normalizar barra final: não pode virar duas linhas
        "https://b.test/api/interpreter",
        "",
        "   ",
        "not-a-url"
    };

    [Fact]
    public void ConfiguredWithoutObservations_AreListedAsUnobservedAndNormalized()
    {
        var rows = MirrorFreshnessViewRow.Merge(Configured, Array.Empty<MirrorFreshnessSnapshot>(), Now);
        Assert.Equal(3, rows.Count); // a (desduplicado), b e not-a-url
        var a = rows.Single(r => r.Endpoint == "https://a.test/api/interpreter");
        Assert.Equal("sem", a.StatusCode);
        Assert.Equal("sem observação", a.StatusLabel);
        Assert.Null(a.BaseTimestampUtc);
        Assert.Null(a.ObservedAtUtc);
        Assert.Equal("a.test", a.Host);
        var b = rows.Single(r => r.Endpoint == "https://b.test/api/interpreter"); // barra final normalizada
        Assert.Equal("b.test", b.Host);
        var weird = rows.Single(r => r.Endpoint == "not-a-url");
        Assert.Equal("not-a-url", weird.Host); // URL inválida exibe como está, sem quebrar
    }

    [Fact]
    public void StoredRowForUnconfiguredEndpoint_IsNotShown()
    {
        var stored = new[] { new MirrorFreshnessSnapshot("https://fora-da-config.test/api/interpreter", Now.AddDays(-1), Now) };
        var rows = MirrorFreshnessViewRow.Merge(new[] { "https://a.test/api/interpreter" }, stored, Now);
        Assert.Single(rows);
        Assert.Equal("sem", rows[0].StatusCode); // a linha do espelho removido fica na tabela como história
    }

    [Fact]
    public void FreshBase_RendersFresco()
    {
        var stored = new[] { new MirrorFreshnessSnapshot("https://a.test/api/interpreter", Now.AddDays(-2), Now.AddMinutes(-5)) };
        var row = MirrorFreshnessViewRow.Merge(new[] { "https://a.test/api/interpreter" }, stored, Now).Single();
        Assert.Equal("fresco", row.StatusCode);
        Assert.Equal("fresco (menos de 14 dias)", row.StatusLabel);
        Assert.NotNull(row.BaseTimestampUtc);
        Assert.NotNull(row.ObservedAtUtc);
    }

    [Fact]
    public void BeyondFourteenDays_RendersAntigo()
    {
        var stored = new[] { new MirrorFreshnessSnapshot("https://a.test/api/interpreter", Now.AddDays(-30), Now) };
        var row = MirrorFreshnessViewRow.Merge(new[] { "https://a.test/api/interpreter" }, stored, Now).Single();
        Assert.Equal("antigo", row.StatusCode);
        Assert.Equal("dado antigo (acima de 14 dias)", row.StatusLabel);
    }

    [Fact]
    public void FourteenDayBoundary_StillRendersFresco()
    {
        var stored = new[] { new MirrorFreshnessSnapshot("https://a.test/api/interpreter", Now.AddDays(-14), Now) };
        var row = MirrorFreshnessViewRow.Merge(new[] { "https://a.test/api/interpreter" }, stored, Now).Single();
        Assert.Equal("fresco", row.StatusCode); // teto excedido só com MAIS que 14 dias (regra da fonte)
    }

    [Fact]
    public void UnreadableAge_RendersIlegivelWithoutThrowing()
    {
        // Regressão: a superfície anterior tratava base nula (idade ilegível) com NullReferenceException.
        var stored = new[] { new MirrorFreshnessSnapshot("https://a.test/api/interpreter", null, Now.AddMinutes(-5)) };
        var row = MirrorFreshnessViewRow.Merge(new[] { "https://a.test/api/interpreter" }, stored, Now).Single();
        Assert.Equal("ilegivel", row.StatusCode);
        Assert.Equal("dado antigo (idade ilegível)", row.StatusLabel);
        Assert.Null(row.BaseTimestampUtc);
        Assert.NotNull(row.ObservedAtUtc); // a observação existe; só a idade que o espelho deu é que é ilegível
    }

    [Fact]
    public void NullOrEmptyConfiguration_YieldsNoRows()
    {
        Assert.Empty(MirrorFreshnessViewRow.Merge(null, Array.Empty<MirrorFreshnessSnapshot>(), Now));
        Assert.Empty(MirrorFreshnessViewRow.Merge(Array.Empty<string>(), Array.Empty<MirrorFreshnessSnapshot>(), Now));
    }
}
