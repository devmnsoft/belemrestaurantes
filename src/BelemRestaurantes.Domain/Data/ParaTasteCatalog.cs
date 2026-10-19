namespace BelemRestaurantes.Domain.Data;

/// <summary>Pratos e ocasiões que a busca pública sabe nomear. Não afirma quem serve o quê.</summary>
public sealed record TasteEntry(string Slug, string Kind, string Title, string Summary, string RegionalType, IReadOnlyList<string> Aliases);

public static class ParaTasteCatalog
{
    public static readonly IReadOnlyList<TasteEntry> Dishes =
    [
        new("tacaca", "prato", "Tacacá", "Caldo de tucupi, jambu, goma e camarão, em geral à tarde. A ficha não confirma o horário desta casa.", "tacaca", ["tacaca", "tacacá", "jambu"]),
        new("manicoba", "prato", "Maniçoba", "Prato de festa e de Círio. A adaptação de casa do portal não substitui o prato do restaurante.", "manicoba", ["manicoba", "maniçoba"]),
        new("pato-no-tucupi", "prato", "Pato no tucupi", "Pato com tucupi e jambu. Só aparece aqui quem tem o prato no cardápio publicado ou no nome.", "pato_tucupi", ["pato no tucupi", "pato ao tucupi"]),
        new("acai", "prato", "Açaí", "Açaí batido no Pará, com farinha, peixe ou o acompanhamento da casa. Não é tigela doce de outra região.", "acai", ["açaí", "acai"]),
        new("peixe", "prato", "Peixe de rio", "Filhote, tambaqui, pirarucu, pescada e caldeirada quando o cadastro diz isso.", "peixe_regional", ["filhote", "tambaqui", "pirarucu", "pescada", "caldeirada", "peixe"]),
        new("vatapa", "prato", "Vatapá", "Vatapá paraense, quando o cardápio ou o nome da casa o cita.", "vatapa", ["vatapa", "vatapá"]),
        new("caranguejo", "prato", "Caranguejo", "Unha, casquinha e caranguejo. A página lista o que o cadastro declara.", "caranguejo", ["caranguejo"]),
        new("cupuacu", "prato", "Cupuaçu", "Doce, suco ou creme de cupuaçu citados no cardápio publicado.", "sobremesa_paraense", ["cupuaçu", "cupuacu"]),
        new("queijo-do-marajo", "prato", "Queijo do Marajó", "Queijo e tapioca quando o cadastro os nomeia.", "sobremesa_paraense", ["queijo do marajó", "queijo do marajo", "marajó"]),
        new("caldeirada", "prato", "Caldeirada", "Caldeirada de peixe citada no nome, na categoria ou no cardápio.", "peixe_regional", ["caldeirada"])
    ];

    public static readonly IReadOnlyList<TasteEntry> Occasions =
    [
        new("almoco-de-domingo", "ocasiao", "Almoço de domingo", "Peixe e caldeirada para a mesa grande. Horário de funcionamento continua o da ficha, com data de conferência.", "peixe_regional", ["caldeirada", "filhote", "tambaqui", "peixe"]),
        new("cirio", "ocasiao", "Círio", "Maniçoba e pato no tucupi, pratos que a cidade associa à festa. Cada casa confirma o próprio cardápio de outubro.", "manicoba", ["maniçoba", "manicoba", "pato no tucupi"]),
        new("turista-48-horas", "ocasiao", "Turista em 48 horas", "Tacacá, açaí e peixe como primeira leitura. O restante do roteiro depende do que está na camada Guia.", "tacaca", ["tacacá", "tacaca", "açaí", "acai", "filhote"]),
        new("almoco-executivo", "ocasiao", "Almoço executivo", "Casas que se apresentam como executivo ou self-service. Preço só entra se o cardápio publicado tiver valor.", "outro", ["executivo", "self-service", "self service", "quilo"]),
        new("familia", "ocasiao", "Com a família", "Peixe e caldeirada, pedidos que costumam servir mesa familiar. Não há estimativa de lotação.", "peixe_regional", ["caldeirada", "filhote", "tambaqui"]),
        new("ilha", "ocasiao", "Ilha e beira de rio", "Combu, Cotijuba, Mosqueiro e Outeiro pelo nome do lugar. Travessia, tarifa e horário só aparecem em Cuia Rotas quando há fonte vigente.", "peixe_regional", ["combu", "cotijuba", "mosqueiro", "outeiro"])
    ];

    public static IReadOnlyList<TasteEntry> All { get; } = Dishes.Concat(Occasions).ToArray();

    public static TasteEntry? Find(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : All.FirstOrDefault(entry => entry.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));
}
