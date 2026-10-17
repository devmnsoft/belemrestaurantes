using MySqlConnector;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Fábrica de conexões MySQL. Centraliza a leitura da connection string e a
/// imposição de parâmetros obrigatórios de segurança (TLS e timeout), para que
/// nenhuma chamada abra conexão sem proteção.
/// </summary>
public sealed class MySqlConnectionFactory
{
    private readonly string _connectionString;
    private readonly MySqlConnectionStringBuilder _builder;

    public MySqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:MySql não configurada. Defina em segredo de ambiente, nunca em appsettings versionado.");
        }

        _connectionString = connectionString;
        _builder = new MySqlConnectionStringBuilder(connectionString);

        if (!string.Equals(_builder.Database, "belrestaurante", StringComparison.Ordinal))
            throw new InvalidOperationException("Somente o banco belrestaurante é autorizado.");
        var local = _builder.Server is "localhost" or "127.0.0.1" or "::1";
        if (!local && _builder.SslMode != MySqlSslMode.VerifyFull)
            throw new InvalidOperationException("MySQL remoto exige SslMode=VerifyFull e certificado confiável.");

        // Salvaguardas aplicadas no cliente: conexões não caem para modo inseguro
        // por engano de digitação, e não ficam penduradas indefinidamente.
        if (_builder.SslMode is not (MySqlSslMode.Required or MySqlSslMode.VerifyCA or MySqlSslMode.VerifyFull))
        {
            throw new InvalidOperationException(
                $"SslMode={_builder.SslMode} não é aceitável em produção. Use Required, VerifyCA ou VerifyFull.");
        }

        // Tempo máximo de espera por resposta do servidor. Sem isso, uma queda
        // de rede prende o ciclo de descoberta por minutos.
        if (_builder.ConnectionTimeout == 0)
        {
            _builder.ConnectionTimeout = 15;
        }

        if (_builder.DefaultCommandTimeout == 0)
        {
            _builder.DefaultCommandTimeout = 30;
        }

        if (!string.IsNullOrEmpty(_builder.UserID) && !_builder.UserID.Contains("root", StringComparison.OrdinalIgnoreCase))
        {
            _builder.Pooling = true;
        }
    }

    public string Database => _builder.Database;

    /// <summary>Abre uma conexão. Não é assíncrono por natureza: o <c>Open</c> é explícito.</summary>
    public MySqlConnection Create()
    {
        var connection = new MySqlConnection(_builder.ConnectionString);
        return connection;
    }

    /// <summary>Abre e devolve uma conexão pronta para uso.</summary>
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = Create();
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Valida a string sem efeitos colaterais, para o health check.</summary>
    public bool LooksUsable => !string.IsNullOrWhiteSpace(_builder.Database) && !string.IsNullOrWhiteSpace(_builder.Server);
}