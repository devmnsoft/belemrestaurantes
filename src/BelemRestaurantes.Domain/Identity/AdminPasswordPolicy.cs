namespace BelemRestaurantes.Domain.Identity;

/// <summary>
/// Politique de senha e bloqueio para as contas administrativas. Fica no domínio
/// para poder ser testada sem subir o ASP.NET Core.
/// </summary>
public static class AdminPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly string[] ForbiddenValues =
    [
        "belemrestaurantes", "mnsoft", "password", "senha", "admin", "qwerty", "123456", "belem"
    ];

    public static IReadOnlyList<string> Validate(string? password)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            failures.Add("A senha é obrigatória.");
            return failures;
        }

        if (password.Length < MinimumLength)
        {
            failures.Add($"A senha deve ter ao menos {MinimumLength} caracteres.");
        }

        if (password.Length > 256)
        {
            failures.Add("A senha excede o tamanho máximo permitido.");
        }

        if (!password.Any(char.IsUpper))
        {
            failures.Add("A senha deve conter ao menos uma letra maiúscula.");
        }

        if (!password.Any(char.IsLower))
        {
            failures.Add("A senha deve conter ao menos uma letra minúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            failures.Add("A senha deve conter ao menos um dígito.");
        }

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
        {
            failures.Add("A senha deve conter ao menos um caractere especial.");
        }

        var lowered = password.ToLowerInvariant();
        if (ForbiddenValues.Any(v => lowered.Contains(v, StringComparison.Ordinal)))
        {
            failures.Add("A senha contém um termo comumente proibido.");
        }

        return failures;
    }

    /// <summary>Determina o novo estado de bloqueio após uma tentativa de login.</summary>
    public static LockoutDecision RegisterFailure(AdminUser user, DateTimeOffset now)
    {
        var attempts = user.AccessFailedCount + 1;
        if (attempts < MaxFailedAttempts)
        {
            return new LockoutDecision(false, now.Add(attempts * TimeSpan.FromSeconds(5)), attempts);
        }

        return new LockoutDecision(true, now.Add(LockoutDuration), attempts);
    }

    public static LockoutDecision RegisterSuccess() => new(false, null, 0);
}

public sealed record LockoutDecision(bool ShouldLockOut, DateTimeOffset? LockoutEndAt, int Attempts);