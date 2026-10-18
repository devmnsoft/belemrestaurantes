namespace BelemRestaurantes.Web.Models;

public sealed record CatalogCategory(string Name, string Icon, string Tone, string Description)
{
    public string LabelKey => "Category_" + Icon;
    public string DescriptionKey => "CategoryDescription_" + Icon;
}

public static class CatalogCategories
{
    public static IReadOnlyList<CatalogCategory> Featured { get; } =
    [
        new("Restaurante", "plate", "red", "O almoço que vira encontro"),
        new("Açaí", "cuia", "green", "Da nossa terra, do seu jeito"),
        new("Pizzaria", "pizza", "gold", "Uma boa pedida para dividir"),
        new("Lanchonete", "snack", "blue", "Uma pausa cheia de sabor"),
        new("Feira e mercado", "market", "gold", "Da banca à mesa"),
        new("Padaria", "bread", "red", "Pão quentinho e boa prosa"),
        new("Cafeteria", "cup", "green", "O tempo de tomar um café"),
        new("Sorveteria", "icecream", "blue", "Para refrescar o passeio")
    ];
}
