using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;

namespace BelemRestaurantes.Infrastructure.Migrations;

/// <summary>Uma migração embutida no assembly.</summary>
public sealed record MigrationScript(string Id, string Name, string Sql)
{
    public string ComputeChecksum()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Sql));
        return Convert.ToHexString(hash);
    }
}

/// <summary>Resultado da aplicação do conjunto de migrações.</summary>
public sealed record MigrationResult(int Applied, IReadOnlyList<string> AppliedIds, IReadOnlyList<string> AlreadyCurrent)
{
    public bool AnythingApplied => Applied > 0;
}

/// <summary>
/// Aplica as migrações embutidas em ordem, registrando cada uma em
/// <c>schema_migrations</c>. Idempotente: rodar duas vezes não faz nada na segunda.
/// </summary>
/// <remarks>
/// O arquivo alterado depois de aplicado é detectado pelo checksum e Reported
/// como erro, em vez de ser silenciosamente ignorado — divergência entre o banco
/// e o código é sempre um incidente.
/// </remarks>
public sealed class MigrationRunner(MySqlConnectionFactory connectionFactory, ILogger<MigrationRunner> logger)
{
    private const string ResourcePrefix = "BelemRestaurantes.Infrastructure.Migrations.Scripts.";

    public async Task<MigrationResult> ApplyAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);

        await EnsureHistoryTableAsync(connection, ct);

        var applied = await connection.QueryAsync<AppliedMigration>(
            new CommandDefinition("SELECT id, checksum FROM schema_migrations", cancellationToken: ct));

        var appliedById = applied.ToDictionary(a => a.id, a => a.checksum, StringComparer.OrdinalIgnoreCase);
        var scripts = LoadScripts();

        var newlyApplied = new List<string>();
        var current = new List<string>();

        foreach (var script in scripts)
        {
            var checksum = script.ComputeChecksum();

            if (appliedById.TryGetValue(script.Id, out var existing))
            {
                if (!string.Equals(existing, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new MigrationChecksumMismatchException(script.Id, existing, checksum);
                }

                current.Add(script.Id);
                continue;
            }

            // DDL no MySQL faz commit implícito; por isso não há transação aqui.
            foreach (var statement in SqlScriptSplitter.Split(script.Sql))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync(ct);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (id, name, checksum, applied_at) VALUES (@id, @name, @checksum, UTC_TIMESTAMP(6))",
                new { id = script.Id, name = script.Name, checksum },
                cancellationToken: ct));

            newlyApplied.Add(script.Id);
            logger.LogInformation("Migração {Id} ({Name}) aplicada.", script.Id, script.Name);
        }

        return new MigrationResult(newlyApplied.Count, newlyApplied, current);
    }

    private static async Task EnsureHistoryTableAsync(MySqlConnection connection, CancellationToken ct)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
              id VARCHAR(64) NOT NULL PRIMARY KEY,
              name VARCHAR(160) NOT NULL,
              checksum CHAR(64) NOT NULL,
              applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = ddl;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Carrega os scripts do assembly, ordenados pelo prefixo numérico.
    /// O nome do recurso precisa começar com número para a ordem ser determinística.
    /// </summary>
    public static IReadOnlyList<MigrationScript> LoadScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;

        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(resourceName =>
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Recurso {resourceName} não pôde ser aberto.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var sql = reader.ReadToEnd();
                var fileName = resourceName[ResourcePrefix.Length..];

                return new MigrationScript(
                    ExtractId(fileName),
                    ExtractName(sql, fileName),
                    sql);
            })
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToArray();

        if (scripts.Length == 0)
        {
            throw new InvalidOperationException(
                "Nenhuma migração encontrada no assembly. Verifique o Include de EmbeddedResource no csproj.");
        }

        return scripts;
    }

    private static string ExtractId(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var separator = name.IndexOf('_');
        return separator > 0 ? name[..separator] : name;
    }

    /// <summary>Usa a linha "-- name: ..." do cabeçalho, caindo para o nome do arquivo.</summary>
    private static string ExtractName(string sql, string fallback)
    {
        var marker = sql.IndexOf("-- name:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return fallback;
        }

        var endOfLine = sql.IndexOf('\n', marker);
        if (endOfLine < 0)
        {
            return fallback;
        }

        var value = sql[(marker + 8)..endOfLine].Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private sealed record AppliedMigration(string id, string checksum);
}

public sealed class MigrationChecksumMismatchException(string id, string existing, string current)
    : Exception(string.Format(
        CultureInfo.InvariantCulture,
        "A migração {0} já aplicada tem checksum {1}, mas o código traz {2}. " +
        "O script foi alterado depois de aplicado. Crie uma nova migração em vez de editar esta.",
        id, existing, current))
{
    public string MigrationId { get; } = id;
}