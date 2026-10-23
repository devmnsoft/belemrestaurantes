# Sprint 74 — a busca da Cuia volta a trazer fontes: orçamento medido na latência real e motivo no log

Execução local em 23/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

A Sprint 73 fechou com a Cuia respondendo bem — mas com um detalhe que o log não explicava: `estado unavailable, 0 fontes`. O item "o rastro `web_search` do drael.sh" ficou marcado como pendência externa, e foi exatamente por ele que esta Sprint começou: se o rastro não vem da API, o gate está certo em rejeitar; se o rastro **vem**, tem bug nosso escondido atrás do "unavailable". A resposta veio de três sondas diretas na API com o payload idêntico ao da Cuia: o rastro `"drael":{"used":["web_search"]}` chega certinho, junto de `finish_reason:"stop"` e de fontes reais (Restaurant Guru, O Liberal, CNN Brasil, UOL). O gate estava sendo vítima de outra coisa.

## O diagnóstico (com as medidas)

1. **A latência real não cabia no orçamento.** Duas sondas completas da busca nativa demoraram **12,95 s** e **17,19 s**. O `CancelAfter(15 s)` fixo na Sprint 73 cortava o stream no meio nas chamadas mais lentas → `OperationCanceledException` interna → `FailureCode="timeout"` → estado `unavailable`. A chamada de 17,2 s simplesmente nunca teve chance.
2. **O motivo já existia — só ninguém lia.** O `WebSearchResult.FailureCode` era produzido nos adapters mas o `CompositeWebSearch` não o propagava quando não havia fontes, e nenhum `LogInformation` da Cuia o imprimia. Resultado: dias de "unavailable" sem causa legível (esta Sprint custou ~40 min de investigação para descobri-lo).
3. **Um limite novo da conta apareceu no caminho.** Chamadas simultâneas à drael.sh recebem `rate_limit_error {"code":"turns_in_flight"}` — a conta limita respostas simultâneas. O cliente já degrada bem (429 → `quota_exhausted`; corpo JSON fora do stream SSE → `invalid_response`), mas o fato é documentado agora no próprio código para ninguém se espantar depois.
4. **Por que o log do HttpClient enganava:** com `HttpCompletionOption.ResponseHeadersRead`, o "End processing HTTP request after Xms" mede **apenas o cabeçalho** (1,7 s / 0,99 s / 0,8 s nas execuções reais) — o tempo do stream fica invisível nessa linha. Foi isso que manteve o problema mascarado.

## O que mudou

- **Orçamento configurável e dimensionado:** `DraelOptions.SearchTimeoutSeconds = 20` (medidas 12,9–17,2 s + folga) e `CompleteTimeoutSeconds = 24`, ambos vinculados à seção `Ai:Drael` (sem segredos novos) e limitados no cliente (`1..60` / `1..120 s`). O comentário no código cita a base de medida.
- **O motivo passou a circular:** `CompositeWebSearch` propaga `FailureCode` quando termina sem fontes; `CuiaAiService` imprime `, motivo {Motivo}` nas duas linhas "Cuia pesquisa:"/("Cuia culinária: pesquisa…") quando há estado degradado — inclusive no caminho culinário, que antes não registrava nada sobre o estado da pesquisa (só em exceção).
- **Comentário do limite concorrente** (`turns_in_flight`) no `DraelClient`, onde a expectativa da conta importa para o dimensionamento.
- **Teste de unidade** `SearchBudgetCoversMeasuredLatencyAndDegradesToTimeoutWhenExceeded`: stream que só começa a responder após o orçamento (fake handler com stream atrasado) deve virar `unavailable`/`timeout` sem fonte inventada; o mesmo fato ancora o default de 20 s.

## O que a validação revelou

1. **`Math.Clamp` virou armadilha de teste:** o piso de 5 s do clamp rejeitou o orçamento de 1 s do teste (que então via o stream de 2 s "completar" e devolvia `empty`). Piso ajustado para 1 s — valor degenerado em produção, caminho real no teste. Lição: clamp é contrato de produção, não filtro de teste.
2. **C# moderno também morde em teste:** `Task.FromResult(new(HttpStatusCode.OK){…})` em corpo de expressão não infere o tipo genérico (CS0411) — `FromResult<T>` precisa do argumento explícito quando o target-typed `new` fica dentro dele.
3. **PowerShell 5.1, caderno contínuo:** `$n:` em string precisa de `${n}` (do contrário lê como drive), e pipe não aceita bloco `{ }` como elemento direto.
4. **A primeira execução "verde silente":** com o binário corrigido, a pergunta culinária respondeu **dentro da etapa culinária** (early return com `Answer != null`) — o log "reutilizada da etapa culinária" só aparece quando a etapa culinária não produz resposta. Adicionar o log do estado na própria etapa culinária eliminou esse ponto cego.

## Validação

- Build Release `--no-incremental` (solução completa, worker parado): **0 avisos / 0 erros**.
- Unidade: **530/530** (base 529 + fato novo de orçamento/timeout).
- Integração local completa: **246 aprovados / 1 ignorado (preexistente) / 0 falhas**.
- Ao vivo (detalhe em [`artifacts/sprint74-live-evidence.txt`](../artifacts/sprint74-live-evidence.txt)): mesma classe de pergunta que dava "unavailable, 0 fontes" saiu em **HTTP 200 em 12,7 s com estado ok e 3 fontes reais renderizadas na página** (Quem, O Liberal, UOL); repetição idêntica em 290 ms (cache); `drael_search` subiu de 3 para 5 (uma unidade por fan-out inédito) com os tetos de geração intocados.

## O que está provado agora — e o que ainda espera (posição honesta)

- **Provado:** a busca nativa do drael.sh rastreia e devolve fontes (stream bruto capturado e versionado); o orçamento cobre a variância medida; a degradação fora do orçamento é nomeada (`timeout`) e visível; a Cuia traz referências públicas de verdade para perguntas culinárias pela primeira vez com cota limpa no dia.
- **Ainda espera (externo):** (1) a amostra de latência tem n=2 por lado (12,9 e 17,2 s) — se a conta do drael.sh ficar sistematicamente lenta, o ajuste é uma chave de configuração, não código; (2) o espelho Overpass continua parado (Sprint 72); (3) a renovação de cotas às 00:00 UTC de 24/10 dá janela limpa para medir a taxa real de timeout com o novo orçamento.
- **Limite declarado da conta:** `turns_in_flight` — duas perguntas culinárias simultâneas com fan-out novo podem competir pelo único slot de resposta; quem perde degrada para `quota_exhausted` sem quebrar a pergunta.

## Operação

Worker parado durante o rebuild e toda a validação; reiniciado com o binário desta Sprint ao final. A chave `ci-log` do CI mantém seu contrato. Nenhuma senha, MFA ou envio de e-mail alterado; evidência gitignoreada entra com `git add -f`.

## Encerramento operacional

A diferença entre "a Cuia pesquisa não responde" e "a Cuia pesquisa respondeu, mas sem fontes" era um inteiro de 15 que devia ser 20 — e um log que ninguém escrevia. A Sprint 74 é o retrato do padrão do projeto: medir antes de mudar, nomear a falha quando ela acontece, e deixar a próxima investigação (de quem vier depois) começar do rastro, não do achismo. As fontes públicas na resposta da Cuia deixam de ser promessa de arquitetura e viram comportamento observado, com URL clicável na página.
