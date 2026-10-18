using System.ComponentModel.DataAnnotations;

namespace BelemRestaurantes.Web.Models;

public sealed class RestaurantSuggestionInput
{
    public long? RestaurantId { get; set; }
    [Required(ErrorMessage = "Conte o nome do restaurante."), StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "Escolha a cidade."), StringLength(100)]
    public string City { get; set; } = "Belém";
    [Url(ErrorMessage = "Informe um endereço HTTPS válido."), StringLength(350)]
    public string? Website { get; set; }
    [StringLength(500, ErrorMessage = "Use até 500 caracteres para sua dica.")]
    public string? Notes { get; set; }
    public string? ContactTrap { get; set; }
}
