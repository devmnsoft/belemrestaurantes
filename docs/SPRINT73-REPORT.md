# Sprint 73 — a Cuia deixa de responder a mesma pergunta duas vezes, para de cair quando o cliente vai embora — e o catálogo ganha sua porta aberta

Execução local em 23/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

Dois pedidos, uma janela. O primeiro veio com um rastro de pilha colado: `TaskCanceledException` subindo por `PublicSiteSearch` → `CuiaAiController.Index` — a Cuia "não responde" de todo quando a pergunta demora e o navegador (ou o dedo) desiste antes. O segundo: "analise o que de melhor o projeto tem e o que mais chama atenção no público no mundo, e implemente aqui". A investigação do primeiro pediu evidência, não intuição — e a resposta do segundo nasceu do que o banco mostrou.

## O diagnóstico (com a cadeia inteira)

1. **Duas buscas compostas por pergunta.** Com pesquisa web ligada, a pergunta culinária rodava o fan-out completo na etapa culinária (prazo 43 s) e o caminho principal rodava **outro** fan-out completo com outra chave de cache (~L210). Latência em dobro e queima de cota gratuita em dobro, todo santo dia.
2. **A busca do Drael roubava a cota da geração.** `DraelWebSearch` reservava no serviço `"cuia_drael"` — o mesmo teto diário que pertence às respostas do modelo. E, por cima, o orçamento da chamada de busca era de 40 s dentro do prazo interativo; ao vivo a busca nativa mediu 10,8 s (completão simples: 1,05 s) — folga boa era 15 s.
3. **O esgotamento de hoje fazia tudo falhar de uma vez:** na hora do relato, `cuia_drael` 20/20, `cuia_gemini` 20/20 e `public_site_search` 30/30 consumidos (`wikipedia_search` 2/30 sobrando, porém fora do escopo culinário). Cada pergunta pagava duas vezes e não trazia fonte nenhuma.
4. **O vazamento da TaskCanceledException:** o frontend corta em 75 s (ou o usuário recarrega); `RequestAborted` cancela o token; a etapa culinária rejeita o cancelamento com `when(ct.IsCancellationRequested){throw;}` (comportamento correto de serviço); o OCE chegava inteiro no MVC; e como o middleware de erro da aplicação roda **dentro** da `DeveloperExceptionPage` implícita em Development, quem venceu foi a página vermelha — não a resposta amigável.
5. **De brinde, a navegação comum ganhava página em branco** em 429/5xx: o `UseStatusCodePages` antigo retornava cedo sem corpo útil para respostas HTML.

## O que mudou

**Metade Cuia (não responder duas vezes, e não cair de pé):**

- **Uma busca por pergunta.** `CulinaryAnswerAsync` agora devolve `(resposta culinária?, resultado web?)`; `AskAsync` captura o resultado e o caminho principal o **reutiliza** (log: "Cuia pesquisa: reutilizada da etapa culinária"). No máximo um fan-out por pergunta — provado por teste com `IWebSearch` contador: exatamente 1 chamada end-to-end.
- **Cota própria para descoberta por busca.** `DraelWebSearch` reserva no serviço `"drael_search"` (teto diário/mensal próprios), separado de `cuia_drael`, que agora só pertence à geração. Fato de unidade novo em `DraelTests`: com o pool esgotado a busca responde `quota_exhausted`, o serviço reservado é exatamente `["drael_search"]`, e nenhum byte sai pela rede.
- **Orçamento de busca 40 s → 15 s** (base medida no item 6 da evidência), comentado no próprio código.
- **Cliente que foi embora não é mais falha de servidor.** `CuiaAiController.Index` trata OCE-com-pedido-aborto e responde a view vazia em silêncio; `Ask` (API POST) responde 408 problem-JSON. Antes, esse mesmo fluxo era a página vermelha do relatório.
- **Páginas de status amigáveis em pt-BR** para navegação HTML (404/410/429/5xx com corpo, dica de "tente novamente" quando aplicável e link para home); o mapeamento JSON dos `/api` ficou intacto.
- **Observabilidade mínima e paga sua conta:** o middleware de falha de API agora grava a exceção completa no log (`LogError(ex, …)`) e não só o nome do tipo — foi isso que escondeu, durante esta Sprint, o defeito de mapeamento do Dapper (abaixo).
- **Front:** aviso honesto após 25 s ("demorando mais do que o normal"), sem abortar; o corte continua nos 75 s originais.

**Metade Atlas Aberto (o diferencial que o banco permitia):**

A análise de diferenciais descartou as duas outras ideias por evidência: roteiros gastronômicos gerados não sobrevivem à realidade do banco (apenas **286 de 16.162** fichas publicadas têm coordenada verificada — 1,8%), e um mapa novo duplicaria o `/mapa` interativo (Leaflet vendido desde a Sprint 44). O que resta é o ativo que o projeto já tem e que quase ninguém expõe: **dados abertos**. Nascem:

- **`/dados-abertos`** — página-hub com contagens reais (publicados / com coordenada / camada Guia), top-15 cidades, metodologia do que entra e do que não entra na exportação, texto da licença ODbL e atalhos para os downloads.
- **`GET /api/dados-abertos/geojson`** — `FeatureCollection` com `Point` apenas onde há coordenada plausível (caixa validada), propriedades em snake_case com os mesmos campos públicos da ficha, envelope `metadata` com `generated_at_utc`, totais e `license: "ODbL-1.0"`; teto de segurança de 250 mil features.
- **`GET /api/dados-abertos.csv`** — BOM UTF-8 (abre certo no Excel pt-BR), cabeçalho estável de 13 colunas, aspas RFC 4180, paginação keyset em lotes de 1.000 linhas (não há `LIMIT` gigante), `Content-Disposition` de download e header `X-Generated-At`.
- **Escolha honesta de tempo:** sem carimbo por linha (a conversão de fuso local no MariaDB 10.11 não é confiável) — o carimbo de geração vai no envelope e no header, e a metodologia diz isso em claro.
- Link do hub no rodapé do `/mapa`.

## O que a validação revelou

1. **Dapper não faz binding de construtor primário de record.** O `OpenDataCatalogRow` nasceu record com construtor posicional e morreu com `InvalidOperationException` no materialization: o `MatchNamesWithUnderscores` casa colunas com *propriedades*, não parâmetros de construtor. Virou record de propriedades `init` (o comentário explica o porquê para a próxima pessoa).
2. **Anonymous type com atalho de propriedade quebra contrato JSON.** `row.Name` no anonymous type vira `"Name"` no GeoJSON (PascalCase) e o teste pegou na hora. Nome explícito snake_case resolveram; o teste de forma (`properties.name`) ficou como guarda.
3. **O decodificador do `HttpClient` come o BOM.** `GetStringAsync` usa `StreamReader` que detecta e remove a marca de ordem de bytes — asserir BOM em string decodificada é falsa verificação. O teste lê os bytes brutos e confere `EF BB BF`.
4. **Host de teste precisa calar os serviços de descoberta.** Com `WebApplicationFactory`, Discovery/Crawler registrados rodariam ciclos reais e poderiam publicar linha no meio do teste de contagem exata — os dois foram desligados via configuração em memória, do mesmo jeito que já se fazia com o banco.
5. **(Caderno xunit 2.9.3, de novo)** sobrecargas de `Assert` com mensagem exigem `Assert.True(cond, msg)`; extensões de registro vivem em namespaces próprias (`AddApplication`/`AddInfrastructure`) — usings faltando viraram os dois CS1061 iniciais do build.

## Validação

- Build Release `--no-incremental` (solução completa, worker parado): **0 avisos / 0 erros**.
- Unidade: **529/529** (base 528 + `SearchUsesItsOwnQuotaSoGenerationIsNotStarved`).
- Integração local completa: **246 aprovados / 1 ignorado (verificação opcional de credenciais, preexistente) / 0 falhas** — base 242 + 1 novo de busca única (fan-out contado: exatamente 1) + 3 novos de dados abertos (GeoJSON idêntico ao SQL direto, forma Point, licença; CSV byte-a-byte do BOM/cabeçalho e contagem de registros == publicados+1; página do hub + rota de snapshot viva).
- Ao vivo (detalhe em [`artifacts/sprint73-live-evidence.txt`](../artifacts/sprint73-live-evidence.txt)): mesma pergunta antes/depois; contadores de cota mostrando `drael_search` +1 por fan-out inédito com `cuia_drael`/`cuia_gemini`/`public_site_search` **congelados**; repetição em cache respondida em 73 ms; duas desconexões forçadas de cliente durante o processamento sem um único `TaskCanceledException`/`Unhandled exception`/`fail:` no log do processo; hub, GeoJSON (286 features = SQL direto) e CSV (1,96 MB) servidos.

## O que está provado agora — e o que ainda espera (posição honesta)

- **Provado:** a pergunta não paga mais duas buscas (contador exato + log "reutilizada"); a busca não rouba cota da geração (contadores congelados + fato de unidade); cliente que sai a meio caminho não derruba o servidor (duas reproduções com overlap garantido); erros de status em navegação têm corpo humano (página nova no lugar de tela em branco); a exportação aberta bate com o banco linha a linha em três grandezas independentes.
- **Honestidade do Atlas Aberto:** a cobertura geográfica é 1,8% das publicações hoje — a página diz, a metodologia explica (coordenada só entra quando verificada), e o crescimento vem do próprio pipeline de geocodificação das Sprints 58–72 drenando a fila. Nenhum número foi arredondado para ficar melhor.
- **Ainda espera (externo):** (1) o rastro `web_search` nas respostas do drael.sh — o gate rejeitou conservadoramente as sondas de hoje (`search_not_verified`: sem o rastro, fonte nenhuma é aceita — nunca inventar URL), então as 0 fontes exibidas são o estado verdadeiro, não uma falha do novo código; (2) os espelhos Overpass voltarem para a demonstração ao vivo da Sprint 72; (3) a renovação de cotas às 00:00 UTC de 24/10 para uma janela limpa de comparações.

## Operação

Worker parado durante todo o rebuild e toda a validação; reiniciado com o binário desta Sprint ao final (detalhe de processo no encerramento). A chave `ci-log` do CI mantém seu contrato (transitória por falha, apagada ao verde). Nenhuma senha, MFA, cota ou envio de e-mail alterado; o arquivo de evidência segue gitignoreado e entra com `git add -f`.

## Encerramento operacional

A Cuia saiu da Sprint com dois contratos que antes eram sorte: **uma única pesquisa por pergunta** e **desculpa educada quando o cliente some** — ambos medidos, não declarados. E o catálogo finalmente tem a porta que os dados abertos pedem: um arquivo estável que um pesquisador, um jornalista ou outro desenvolvedor pode baixar sem formulário, sem login e sem licença-surpresa. O próximo salto em visibilidade não será um feature novo — será a fila de geocodificação encurtando os 98,2% que ainda não têm coordenada, transformando o "1,8%" da página em algo que valha a pena recortar.
