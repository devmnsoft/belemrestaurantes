using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BelemRestaurantes.Worker;

/// <summary>
/// Emite códigos de recuperação no console local. Não altera a senha, não desliga o MFA
/// e não grava o código em claro.
/// </summary>
public static class AdminRecoveryIssue
{
    public static async Task RunAsync(IServiceProvider services, bool authorized)
    {
        if (!OperatingSystem.IsWindows() || !authorized || !services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            throw new InvalidOperationException("A emissão exige Windows, Development e --authorize-homologation.");
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("A emissão exige um console interativo local.");

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        Console.Write("Usuário administrativo: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new InvalidOperationException("Informe o nome de usuário.");

        var users = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var user = await users.FindByNameAsync(name, default);
        if (user is null || !user.TwoFactorEnabled)
            throw new InvalidOperationException("A conta precisa existir, estar ativa e já ter MFA. O MFA não será desligado.");

        var codes = RecoveryCodes.Generate(8);
        await users.ReplaceRecoveryCodesAsync(user.Id, codes.Select(code => RecoveryCodes.Hash(RecoveryCodes.Normalize(code))).ToArray(), default);
        MySqlAuditWriter.CurrentActor.Value = "local-recovery";
        try
        {
            await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteAsync(
                "admin.recovery_codes_reissued", "admin_user", null, JsonSerializer.Serialize(new { userId = user.Id, count = codes.Count }), default);
        }
        finally
        {
            MySqlAuditWriter.CurrentActor.Value = null;
        }

        Console.WriteLine("Guarde estes códigos agora. Cada um funciona uma vez. O MFA continua obrigatório.");
        foreach (var code in codes)
            Console.WriteLine(code);
    }
}
