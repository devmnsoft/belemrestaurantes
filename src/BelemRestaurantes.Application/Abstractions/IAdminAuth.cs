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

/// <summary>
/// Cifra segredos sensíveis em repouso (TOTP). O segredo TOTP em texto puro só
/// existe em memória durante o login/enrolamento — nunca no banco.
/// </summary>
public interface IAdminSecretProtector
{
    byte[] Protect(string plainText);

    /// <summary>Lança exceção se os bytes não forem um valor cifrado válido.</summary>
    string Unprotect(byte[] protectedBytes);
}

/// <summary>Esquema de autenticação de cookie administrativo (nome único, sem "mágica").</summary>
public static class AdminAuthDefaults
{
    public const string Scheme = "MNSOSTIdentity";
}