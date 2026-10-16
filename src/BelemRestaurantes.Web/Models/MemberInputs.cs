using System.ComponentModel.DataAnnotations;
namespace BelemRestaurantes.Web.Models;

public sealed class RegisterInput
{
    [Required, StringLength(160, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [Required, StringLength(256, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    public bool AcceptTerms { get; set; }
    public string? Website { get; set; }
}
public sealed class MemberLoginInput
{
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [Required, StringLength(256), DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool Remember { get; set; }
    public string? ReturnUrl { get; set; }
}
public sealed class EmailInput
{
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
}
public sealed class ResetInput
{
    [Required, StringLength(64, MinimumLength = 64)] public string Token { get; set; } = "";
    [Required, StringLength(256, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
