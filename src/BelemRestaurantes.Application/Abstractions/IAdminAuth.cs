namespace BelemRestaurantes.Application.Abstractions;

/// <summary>Gerador de token TOTP (RFC 6238), para o segundo fator de administradores.</summary>
public interface ITotpProvider
{
    string GenerateSecret();

    /// <summary>Código de 6 dígitos válido no instante indicado.</summary>
    string GenerateFor(DateTimeOffset instant, string base32Secret);

    /// <summary>Valida o código com janela de ±1 período.</summary>
    bool Validate(string code, string base32Secret, DateTimeOffset now);
}

/// <summary>Verificação de senha administrativa, independente de Identity.</summary>
public interface IPasswordVerifier
{
    string Hash(string password);

    bool Verify(string password, string hash);
}