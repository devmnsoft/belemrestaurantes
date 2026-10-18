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
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Sql.Replace("\r\n", "\n")));
        return Convert.ToHexString(hash);
    }

    // Checkouts Windows antigos gravaram o checksum do mesmo SQL em CRLF.
    // Aceitamos apenas as duas representações de fim de linha, nunca outro SQL.
    public bool MatchesChecksum(string checksum) =>
        string.Equals(checksum, ComputeChecksum(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(checksum, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Sql.Replace("\r\n", "\n").Replace("\n", "\r\n")))), StringComparison.OrdinalIgnoreCase);
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

        var locked = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT GET_LOCK('belrestaurante:migrations', 30)", cancellationToken: ct));
        if (locked != 1) throw new InvalidOperationException("Outra execução de migrações está ativa.");
        try
        {
            await EnsureHistoryTableAsync(connection, ct);
            await connection.ExecuteAsync(new CommandDefinition("""
                CREATE TABLE IF NOT EXISTS schema_migration_attempts (
                  id VARCHAR(64) PRIMARY KEY, checksum CHAR(64) NOT NULL,
                  started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                  completed_at DATETIME(6) NULL
                ) ENGINE=InnoDB
                """, cancellationToken: ct));

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
                    if (!script.MatchesChecksum(existing))
                    {
                        throw new MigrationChecksumMismatchException(script.Id, existing, checksum);
                    }

                    current.Add(script.Id);
                    continue;
                }

                // DDL no MySQL faz commit implícito; por isso não há transação aqui.
                var interrupted = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM schema_migration_attempts WHERE id = @id AND completed_at IS NULL",
                    new { id = script.Id }, cancellationToken: ct));
                if (interrupted > 0)
                    throw new InvalidOperationException($"Migração {script.Id} interrompida. Restaure backup ou reconcilie o DDL antes de liberar a tentativa.");
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_migration_attempts (id, checksum) VALUES (@id, @checksum)",
                    new { id = script.Id, checksum }, cancellationToken: ct));

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

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE schema_migration_attempts SET completed_at = UTC_TIMESTAMP(6) WHERE id = @id",
                    new { id = script.Id }, cancellationToken: ct));
                newlyApplied.Add(script.Id);
                logger.LogInformation("Migração {Id} ({Name}) aplicada.", script.Id, script.Name);
            }

            return new MigrationResult(newlyApplied.Count, newlyApplied, current);
        }
        finally
        {
            await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT RELEASE_LOCK('belrestaurante:migrations')", cancellationToken: CancellationToken.None));
        }
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
