using BelemRestaurantes.Worker;
using Xunit;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 68 — detecção de sobrescrita da flag <c>Database:ApplyMigrations</c> por fontes de maior
/// precedência. Caso real que motivou a Sprint: o cofre local de user-secrets do Worker carregava
/// <c>"Database:ApplyMigrations": false</c>, silenciando a aplicação de migrações em todo boot apesar de
/// os dois appsettings versionados declararem <c>true</c>.</summary>
public sealed class Sprint68MigrationFlagOverrideTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameValue_IsNotAnOverride(bool value)
        => Assert.False(ConfigurationDiagnosticsService.IsValueOverridden(value, value));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DifferentValues_IsAnOverride(bool effective, bool appSettings)
        => Assert.True(ConfigurationDiagnosticsService.IsValueOverridden(effective, appSettings));

    [Theory]
    [InlineData(null, true)]   // chave ausente em todo lugar -> nada a comparar
    [InlineData(null, false)]
    [InlineData(true, null)]   // appsettings sem a chave -> padrão do código vale, não é sobrescrita
    [InlineData(false, null)]
    [InlineData(null, null)]
    public void MissingValue_NeverAnOverride(bool? effective, bool? appSettings)
        => Assert.False(ConfigurationDiagnosticsService.IsValueOverridden(effective, appSettings));
}
