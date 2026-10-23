# Sprint 51 — Pesquisa com contexto e descoberta contínua

Entrega local em 20/10/2026. Sem commit, deploy, contas novas, contratação ou aumento de cota. Credenciais existentes permaneceram nos cofres locais.

## Resultado e limites atuais

O fluxo da Cuia agora conserva a pergunta inicial e as duas mais recentes, recupera o estabelecimento em perguntas curtas, combina até oito referências e produz uma resposta com evidências quando a geração não está disponível. Dados de cardápio são compostos a partir de campos documentados, sem transformar pratos típicos em pratos de uma casa específica.

A entrega NÃO significa que Google/Bing, busca geral ou geração Gemini estejam homologados ao vivo: não existe chave Tavily/Brave nos cofres e a cota configurada para IA foi atingida na verificação real. A inspeção somente leitura confirmou `cuia_gemini` com 20 de 20 tentativas diárias consumidas, já antes desta Sprint. A indisponibilidade de geração nesta homologação decorre desse limite local; não se declara que a cota da conta Google foi medida. Esses bloqueios não foram contornados por cobrança. O que está funcionando localmente é o catálogo, o índice revisado, a pesquisa enciclopédica limitada e os novos mecanismos de contexto/apresentação.

## Diagnóstico

- Cardápios antes ignoravam toda a geração e, sobretudo, não recuperavam evidências de páginas novas. Foi acrescentada recuperação de dados estruturados em fontes externas. A composição final de pratos continua restrita aos campos documentados.
- A pesquisa recebia perguntas anteriores concatenadas sem distinguir continuação de mudança de assunto. `CuiaConversation` agora identifica continuações e recupera uma pergunta com entidade, sem anexar assunto anterior a uma pergunta nova explícita. A interface conserva a pergunta inicial em vez de descartá-la após três interações. A classificação é heurística e ainda pode precisar de esclarecimento em perguntas ambíguas.
- Resultados enciclopédicos irrelevantes podiam aparecer em perguntas comerciais. A verificação real encontrou uma página histórica numa pergunta sobre o Na Maré; referências do lugar passaram a ter prioridade e resultados enciclopédicos são descartados nesses contextos. Existe teste de regressão desse caso.
- O cliente seguro tinha timeout de envio que não limitava toda a leitura e selecionava apenas o primeiro IP validado. Agora o prazo abrange DNS, redirecionamentos e corpo; o socket pode tentar os endereços públicos validados. Cancelamento do solicitante é propagado. Isso corrige fragilidades, mas não prova que toda instabilidade anterior tivesse uma única causa de rede.
- Há logs de provedor, etapa de pesquisa, estado, quantidade e duração; não registram pergunta ou credenciais nesses novos eventos. Falhas da pesquisa pública identificam tipo de erro de rede ou prazo.

## Provedores de pesquisa

Mantido Tavily básico, ampliado para até oito resultados. Adicionado adaptador Brave, desabilitado por padrão. Cadeia: Tavily configurado → Brave se Tavily não configurado → Wikipédia se nenhum provedor geral estiver configurado. Falha ou quota de um provedor configurado não acionam outro para burlar limites.

O Brave exige `WebSearch:Brave:Enabled`, `FreeTierConfirmed`, `ApiKey`, `DailyRequestLimit` e `MonthlyRequestLimit`. Padrões: 20/dia e 200/mês; não foi criada conta nem configurada chave. É responsabilidade do ambiente confirmar uma franquia sem cobrança antes de habilitar. A documentação atual informa crédito mensal e ativação de plano com cartão; portanto não é apresentado como alternativa gratuita já disponível nesta máquina.

Não foi implementado um cliente da antiga API Bing, encerrada em agosto de 2025. O Google Custom Search está fechado a novos clientes e tem encerramento anunciado para janeiro de 2027; também exige chave e mecanismo de busca próprio. Grounding Google no modelo Gemini 3.1 Flash-Lite não está disponível no nível gratuito consultado. A chave Gemini não foi reutilizada como se habilitasse esses produtos.

A composição usa cache limitado, conserva a data original e reúne chamadas concorrentes. Fontes em cache são identificadas. Erros têm retenção curta. A Wikipédia continua limitada a um endpoint fixo; foi acrescentada verificação de correspondência de termos, mas isso não substitui a identificação de entidade.

## Evidências e interface

`ResearchEvidence` consulta até três páginas dos resultados, em prazo total de 10 segundos, usando o cliente com proteção SSRF e a política robots existente. Tipos, tamanho, DNS e redirecionamentos continuam limitados. Se uma origem falhar, os trechos de pesquisa disponíveis são preservados. Não baixa conteúdo privado nem contorna bloqueios.

Dados estruturados de restaurante podem fornecer nome, cidade explícita, endereço, pratos e horários declarados. Correspondência do domínio declarado é um indício da fonte, não uma certificação de propriedade ou de funcionamento atual. Páginas editoriais não são automaticamente tratadas como cardápio oficial.

Fontes conservam publicador, consulta, publicação quando disponível, tipo e estado de recuperação. Respostas parciais mostram trechos curtos atribuídos, endereço disponível e a lacuna concreta de preço ou transporte. A IA recebe fontes numeradas, contexto curto e instruções que separam evidências de comandos. Respostas externas geradas sem citações válidas são recusadas; isso reduz falhas, mas não é prova automática de veracidade semântica de toda frase.

A página e o widget exibem até oito resultados, citações clicáveis no JavaScript, metadados e mensagens de falha distintas. A página recebeu cancelamento explícito; fechar o widget também cancela. Foram mantidos limites por sessão e proteção contra envio duplicado.

Links Google, Bing, VEJA, Instagram, TikTok e LinkedIn abrem pesquisas no navegador com o contexto da pergunta. Estão explicitamente rotulados como ações do usuário, não como consultas realizadas pelo servidor. Redes sociais podem aparecer como resultados indexados de um provedor geral; o recuperador não raspa essas plataformas. Nenhuma integração privada, nota agregada ou comentário em massa foi inventado. Não foram adicionadas novas fotos ou vídeos sem condição de uso conhecida.

## Robô e publicação automática

Migração 040 cria `research_jobs`: uma fila durável de pesquisas por alvo e dia, com estado, lease, resultado, quantidade de fontes e de páginas encaminhadas. Não contém conversas de usuários. A reserva é transacional e respeita alvos pausados; leases vencidos podem ser retomados.

`ResearchHostedService` roda a cada 30 minutos, com dois jobs por ciclo no ambiente local. Só consulta busca geral se houver um provedor confirmado/configurado. Encaminha até três páginas com identidade estruturada e endereço para o crawler existente. Sem provedor, registra a dependência e deixa a descoberta/crawler já existentes continuarem. A IA de enriquecimento já existente foi preservada; a nova fila não cria geração ilimitada.

Publicação automática de novas descobertas exige:

- Conector estruturado ativo.
- Restaurante identificado por JSON-LD, cidade explícita do catálogo oficial do Pará e URL declarada no mesmo domínio HTTPS da fonte.
- Nome e endereço suficientes.
- Ausência de conflito e de possível duplicata.
- Validação transacional final do repositório de moderação.

Limite local de três publicações por ciclo por instância. Publica como ficha, sem promover para Guia. Usa o fluxo de moderação existente, com auditoria na mesma transação; suspensão administrativa permite reversão. Mídias continuam sujeitas a regras próprias e não são liberadas por essa publicação. Exceções registram a razão na fila.

Habilitados somente nos cofres locais: `Crawler:AutoPublication:Enabled=true`, `MaxPerCycle=3`; no Worker também `Worker:ResearchEnabled=true`, `ResearchBatchSize=2`. Outros ambientes permanecem opt-in. Não foi homologada publicação real de estabelecimentos descobertos por esta nova fila: o teste criou e removeu apenas sua própria ficha, e a pesquisa geral ainda depende de credencial.

O painel `/admin/crawler` mostra os últimos jobs de pesquisa; `/admin/connectors` diferencia pesquisa geral e limitada. Os controles administrativos e MFA foram preservados.

## Verificações

- Build Release final: zero erros e avisos.
- Suíte unitária: 374 aprovados.
- Suíte de integração: 212 aprovados, um teste preexistente ignorado. Após o ajuste final de correspondência estrita de domínio, os dois testes de fila/publicação foram repetidos e passaram (`integration-security-final.log`).
- UI: 42 cenários sem violações Axe nos critérios executados, overflow ou erros JavaScript.
- Mobile: 36 cenários sem overflow ou erros JavaScript.
- Mais três renderizações de respostas reais gravadas, também sem violações Axe ou overflow (`recorded-render.json`), sem consumir novas chamadas. Captura revisada: `recorded-answer-3.png`.
- A conversa real final respondeu às três perguntas com HTTP 200, conservou Na Maré e retornou o endereço da referência revisada na terceira pergunta. O resultado enciclopédico irrelevante foi descartado (`externalSearch=irrelevant`). A resposta de pratos identificou uma menção da VEJA, sem afirmar cardápio vigente; preços atuais ficaram explicitamente não verificados. As duas perguntas seguintes registraram `quota_exhausted`, sem geração por IA.

Evidências:

- `artifacts/sprint51/build-final.log`: compilação.
- `unit-final.log` e `integration-final.log`: suítes automatizadas.
- `ui/report.json`: 42 cenários de interface, incluindo fontes inseguras, contexto de três perguntas e cancelamento, com Axe e overflow.
- `platforms/report.json`: 36 cenários em Chromium/Pixel 7 e WebKit/iPhone 13 emulados, nas duas orientações. Não são testes em aparelhos físicos.
- `search-live.log`: pesquisa pública real, HTTP 200 em aproximadamente um segundo.
- `live-conversation.json`: conversa real de três perguntas, sem substituir respostas por mocks.

Testes automatizados foram explicitamente isolados das chaves de IA e dos serviços de busca externos; chamadas reais ficam separadas. O teste de publicação verifica auditoria, idempotência, permanência como ficha e suspensão. O teste da fila verifica pausa, reserva única e persistência do resultado.

## Dependências pendentes

1. Para pesquisa geral no servidor: configurar uma conta/chave Tavily ou Brave com franquia gratuita confirmada. Não colar a chave em código ou no chat.
2. Para síntese generativa: aguardar a renovação da cota Gemini ou configurar outro provedor gratuito autorizado. Não aumentamos o limite para mascarar a indisponibilidade.
3. Redes sociais, avaliações, fotos e vídeos dependem das integrações públicas/oficiais e permissões correspondentes; os links de pesquisa não equivalem a essas integrações.
4. As revisões editoriais e fontes antigas continuam exigindo confirmação de atualidade, especialmente para preços, horários e transporte.

## Documentação oficial consultada

- [Encerramento das Bing Search APIs](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement).
- [Google Custom Search: disponibilidade e encerramento](https://developers.google.com/custom-search/v1/overview).
- [Grounding com Google Search](https://ai.google.dev/gemini-api/docs/google-search).
- [Preços e nível gratuito Gemini](https://ai.google.dev/gemini-api/docs/pricing).
- [Brave: autenticação e início](https://api-dashboard.search.brave.com/documentation/quickstart).
- [Brave: API de pesquisa](https://api-dashboard.search.brave.com/app/documentation/web-search/get-started).

Não foram instalados serviços, alteradas contas ou publicadas mudanças no repositório remoto.


## Estado local final

Worker retomado: PID 11952, supervisor 52888. Heartbeat conferido em `artifacts/sprint51/worker-final.json`. O serviço de pesquisa informa que aguarda um provedor geral; o crawler e a descoberta existentes continuam. A revisão Web usa `http://localhost:5180/`. Para usar a versão atual no Visual Studio, pare a execução antiga, recompile e reinicie o perfil Web. Não foi instalado serviço de produção.
