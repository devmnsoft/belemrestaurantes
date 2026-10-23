namespace BelemRestaurantes.Application.Formatting;

public static class CuiaSearchPresentation
{
    public static string Message(string? status) => status switch
    {
        "public_sources" => "Pesquisa em conhecimento público da Wikipédia, com cobertura limitada. Não confirma cardápios, preços ou horários atuais.",
        "public_sources_cached" => "Resultados anteriores da Wikipédia, reutilizados do cache. Confira a data de consulta; não confirmam informações atuais do estabelecimento.",
        "indexed_sources" => "Referências previamente consultadas e revisadas. Não há resultados novos de pesquisa aberta nesta resposta.",
        "mixed_sources" => "Referências revisadas e fontes externas. Confira a origem e a data de cada fonte.",
        "irrelevant" => "Os resultados externos encontrados não sustentavam a pergunta sobre este estabelecimento e foram descartados.",
        "collected_sources" => "Referências coletadas anteriormente pelo robô e pelo acervo do portal. Confira a data e a publicação original; não é uma pesquisa nova ao vivo.",
        "cached" => "Resultados reutilizados do cache; a data original de consulta foi preservada.",
        "credential_error" => "O provedor de pesquisa precisa de ajuste na credencial.",
        "ok" => "Resultados da pesquisa externa. Consulte as fontes antes de planejar sua visita.",
        "empty" => "A pesquisa foi executada, mas não encontrou fontes utilizáveis para esta pergunta.",
        "not_configured" => "A pesquisa externa não está habilitada neste ambiente. Você pode explorar os roteiros e as referências já revisadas.",
        "quota_exhausted" => "O limite de pesquisa externa foi atingido. Os roteiros e o catálogo continuam disponíveis.",
        "unavailable" => "O serviço de pesquisa externa não respondeu nesta tentativa. Você pode tentar novamente ou explorar os roteiros.",
        _ => "Confira a origem das informações antes de sair."
    };
}
