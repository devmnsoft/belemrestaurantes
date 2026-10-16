# Estratégia de conectores

**Google Places**: integração inicial de descoberta por texto com fields mask mínima; IDs são usados para reconciliação. Fotos, atributos e avaliações não devem ser replicados persistentemente sem confirmação das políticas de uso e atribuição.

**Instagram/Facebook**: exigir app Meta, token de sistema com permissões aprovadas, contas profissionais autorizadas, webhook onde aplicável; jamais usar sessão pessoal, cookies ou scraping de perfis privados.

**TikTok**: apenas conteúdo público cujo uso/incorporação seja autorizado pela API, oEmbed ou pelo proprietário.

**YouTube**: YouTube Data API para metadados e player oficial para vídeos. A busca tem quotas e precisa guardar IDs e links permitidos.

**Gemini/Groq/DeepSeek**: interface de provedores por serviço backend, escolha por tarefa, limites financeiros, segredos por ambiente e tratamento de saída como conteúdo não confiável. Não há adaptadores ativos nesta etapa.

**Transporte**: Google Maps pode abrir rotas de transporte; para linhas, tarifas e horários verificáveis integrar dados GTFS/GTFS-RT ou fornecedores licenciados e expor origem e data de cada tarifa.

O painel deverá mostrar estado de cada conector (não configurado / ativo / degradado), último sucesso, custo e atribuições legais.
