namespace BelemRestaurantes.Domain.Rules;

/// <summary>
/// Separa lugar para comer de comércio, distribuição e serviço que não recebe o público à mesa.
/// Cadastro empresarial alimentício não vira restaurante aberto.
/// </summary>
public static class FoodVenueRules
{
    public static bool IsRestaurantOpenToPublic(string? category) => category is
        "Restaurante" or "Bar" or "Lanchonete" or "Cafeteria" or "Açaí" or "Sorveteria" or "Pizzaria" or "Padaria"
        or "Tacacá" or "Hamburgueria" or "Churrascaria" or "Pastelaria" or "Confeitaria" or "Buffet"
        or "Comida de rua" or "Cantina" or "Praça de alimentação" or "Feira e mercado" or "Empório";

    public static string? FromCnae(int? cnae) => cnae switch
    {
        5611201 => "Restaurante",
        5611203 => "Lanchonete",
        1091102 or 4721102 => "Padaria",
        5611204 or 5611205 => "Bar",
        5612100 => "Comida de rua",
        4721104 => "Confeitaria",
        5620102 => "Buffet",
        5620103 => "Cantina",
        4722902 => "Peixaria",
        4724500 => "Hortifrúti",
        4711301 or 4711302 => "Supermercado",
        4712100 or 4721103 or 4722901 or 4723700 or 4729699 => "Comércio alimentício",
        5620101 or 5620104 => "Outros serviços alimentares",
        >= 4630000 and <= 4639999 => "Distribuidor",
        _ => null
    };

    /// <summary>Distribuidor não entra no catálogo de quem vai comer.</summary>
    public static bool ImportIntoDiningCatalog(string? category) =>
        category is not null && category != "Distribuidor";
}
