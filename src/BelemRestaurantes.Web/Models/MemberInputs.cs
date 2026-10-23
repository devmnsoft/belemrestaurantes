using System.ComponentModel.DataAnnotations;
namespace BelemRestaurantes.Web.Models;

public sealed class RegisterInput : IValidatableObject
{
    [Required(ErrorMessage="Informe seu nome."), StringLength(160, MinimumLength = 2, ErrorMessage="O nome deve ter de 2 a 160 caracteres.")] public string FullName { get; set; } = "";
    private string email = "";
    [Required(ErrorMessage="Informe seu e-mail."), EmailAddress(ErrorMessage="Informe um e-mail válido."), StringLength(200)] public string Email { get => email; set => email = value?.Trim() ?? ""; }
    [Required, StringLength(256, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage="A confirmação deve ser igual à senha."), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    [Required, StringLength(14)] public string Cpf {get;set;}="";
    [StringLength(18)] public string? Cnpj {get;set;}
    [StringLength(160)] public string? OrganizationName {get;set;}
    public bool AcceptTerms { get; set; }
    public string? Website { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!BelemRestaurantes.Domain.Identity.BrazilianDocument.IsCpf(Cpf)) yield return new("CPF inválido. Confira os 11 dígitos do documento.", [nameof(Cpf)]);
        if (!AcceptTerms) yield return new("Aceite os termos para criar sua conta.", [nameof(AcceptTerms)]);
        foreach (var error in BelemRestaurantes.Domain.Identity.AdminPasswordPolicy.Validate(Password)) yield return new(error, [nameof(Password)]);
        if (!string.IsNullOrWhiteSpace(Cnpj))
        {
            if (!BelemRestaurantes.Domain.Identity.BrazilianDocument.IsCnpj(Cnpj)) yield return new("CNPJ inválido. Confira os caracteres e dígitos verificadores.", [nameof(Cnpj)]);
            if (string.IsNullOrWhiteSpace(OrganizationName) || OrganizationName.Trim().Length < 2) yield return new("Informe a razão social da empresa.", [nameof(OrganizationName)]);
        }
    }
}
public sealed class MemberLoginInput : IValidatableObject
{
    private string email="";
    [Required, StringLength(200)] public string Email { get=>email; set=>email=value?.Trim()??""; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if(!new EmailAddressAttribute().IsValid(Email)&&!BelemRestaurantes.Domain.Identity.BrazilianDocument.IsCpf(Email))yield return new("Informe um e-mail ou CPF válido.",[nameof(Email)]);
    }
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
