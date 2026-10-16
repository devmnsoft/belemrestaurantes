using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;

namespace BelemRestaurantes.Web;

internal static class AdminMaintenance
{
    internal static async Task RunAsync(IServiceProvider services, bool migrate)
    {
        using var scope = services.CreateScope();
        if (migrate)
        {
            await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
            return;
        }
        if (Console.IsInputRedirected) throw new InvalidOperationException("Provisionamento exige console interativo local.");
        Console.Write("Usuário: ");
        var name = Console.ReadLine()?.Trim();
        Console.Write("E-mail: ");
        var email = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
            throw new InvalidOperationException("Nome ou e-mail inválido.");
        Console.Write("Senha (oculta): ");
        var password = ReadPassword();
        Console.Write("Confirme a senha: ");
        if (password != ReadPassword() || AdminPasswordPolicy.Validate(password).Count > 0)
            throw new InvalidOperationException("Senhas diferentes ou fora da política.");
        var users = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var verifier = scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var id = Guid.NewGuid().ToString("N");
        var record = new AdminUserRecord(id, name, email, verifier.Hash(password), false, null, 0, null, null);
        if (!await users.CreateAsync(record, [AdminRoles.PlatformAdmin], default))
            throw new InvalidOperationException("Não foi possível criar o administrador.");
        MySqlAuditWriter.CurrentActor.Value = "local-maintenance";
        try
        {
            await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteAsync(
                "admin_provisioned", "admin_user", null, System.Text.Json.JsonSerializer.Serialize(new { userId = id }), default);
        }
        finally { MySqlAuditWriter.CurrentActor.Value = null; }
        Console.WriteLine("Conta criada. Configure o TOTP no primeiro login; nenhum acesso é liberado sem MFA.");
    }

    private static string ReadPassword()
    {
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; continue; }
            if (!char.IsControl(key.KeyChar) && buffer.Length < 256) buffer.Append(key.KeyChar);
        }
    }
}
