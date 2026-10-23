namespace BelemRestaurantes.Domain.Identity;

public sealed class MemberRegistrationValidationException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}
