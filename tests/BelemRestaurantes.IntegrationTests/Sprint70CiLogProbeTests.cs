using System;
using System.Threading.Tasks;
using Xunit;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Sprint 70 — sonda temporária e deliberadamente vermelha: existe só para provar, com uma falha
/// real no CI, que o pipeline de diagnóstico publica o resultado na tag anotada <c>ci-log</c> antes de
/// falhar o job. O gatilho é o próprio contexto do GitHub Actions: em execução de push para branch
/// (<c>GITHUB_REF=refs/heads/*</c> — o caso do <c>main</c>) a sonda falha; em tag de aquecimento
/// (<c>refs/tags/*</c>), em pull request e em execução local (<c>GITHUB_REF</c> ausente) ela passa —
/// assim o mesmo SHA pode primeiro aquecer o check obrigatório verde e depois assumir o vermelho
/// controlado quando chega ao <c>main</c>. Removida no commit seguinte (docs/SPRINT70-REPORT.md).</summary>
public sealed class Sprint70CiLogProbeTests
{
    [Fact]
    public async Task Probe_FailsOnlyOnBranchPush_UntilRemoved()
    {
        await Task.CompletedTask;
        var @ref = Environment.GetEnvironmentVariable("GITHUB_REF") ?? string.Empty;
        if (!@ref.StartsWith("refs/heads/", StringComparison.Ordinal)) return;
        Assert.Fail("Sprint 70 — sonda controlada para exercitar o pipeline ci-log; removida no commit seguinte.");
    }
}
