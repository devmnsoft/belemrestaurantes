# Sprint 67 — frescor sticky dos espelhos Overpass: adiamento sem custo quando o espelho já provou que está velho

Execução local em 23/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

O encerramento da Sprint 66 deixava documentada a alavanca seguinte: "o retry antigo agora custa apenas a unidade do próprio Overpass… a sonda de frescor do espelho com estado persistente ('sticky') é a alavanca seguinte natural". As medições confirmaram o desperdício:

- **87 fichas pendentes presas em `overpass_stale_data`** — cada retry pagava uma unidade do teto diário para perguntar a um espelho cujo dado já estava comprovadamente mais velho que 14 dias, e o espelho devolvia o mesmo veredicto.
- Sonda manual dos três espelhos em 23/10 às 11:04 UTC: `overpass-api.de` e `overpass.kumi.systems` **tempo limite excedido** (> 25 s) e `overpass.osm.ch` respondendo com `osm3s.timestamp_osm_base = "117936"` — nem ISO-8601 nem epoch plausível, logo idade **ilegível → conservadoramente stale** pela regra da Sprint 62. Nenhum espelho servia dado fresco e legível no momento.
- Como o failover para em **primeira resposta HTTP bem-sucedida**, um espelho antigo-rápido responde stale para cada tarefa e os espelhos eventualmente frescos nunca são consultados — a informação "este espelho está velho" era redescoberta do zero a cada requisição.

## O que mudou

- **`OverpassOptions.StaleMirrorWindowMinutes`** (padrão 60; entrada explícita `"StaleMirrorWindowMinutes": 60` na seção `Overpass` do `appsettings.json` do Worker): janela de validade da observação "este espelho respondeu com dado mais velho que 14 dias".
- **`OverpassBuildingSource`**:
  - armazenamento por processo da observação por espelho (`ConcurrentDictionary`, mesmo escopo do `_lastGood` existente), gravada por **toda resposta bem-sucedida** — fresca apaga o stale anterior, idade ilegível entra conservadora;
  - pulo por espelho dentro do loop: espelho com observação stale dentro da janela não recebe requisição (nem consome cadência mínima nem o gate);
  - **early-return** quando todos os espelhos estão confirmados stale dentro da janela: resposta `available + stale + vazio` com **zero requisições**;
  - `ReadBaseTimestamp` extraído de `IsDataStale` (semântica idêntica — a regressão total dos testes da Sprint 62 validou); `RecordMirrorObservation` e `ResetMirrorObservations` públicos para teste/diagnóstico.
- **`IOsmBuildingSource.AllMirrorsKnownStale`**: membro novo — `true` quando todo espelho configurado tem confirmação recente de dado velha.
- **Ciclo do Worker** (`GeocodingHostedService`): o curto-circuito consulta `AllMirrorsKnownStale` **antes** de `TryReserveDailyAsync` — ficha adiada como `overpass_stale_data` sem consumir unidade do teto nem enviar requisição. A ordem do restante do bloco Overpass (reserva → consulta → avaliação) não mudou.

## Decisões

- **Escopo de processo, sem migração**: as instâncias do Worker nunca sobrepõem ciclos (lock/heartbeat), então o estado não precisa ser compartilhado entre processos — mesmo padrão do `_lastGood` da Sprint 62. Após reinício, a sonda se repete no máximo uma vez por espelho: direção conservadora (probe extra, nunca falsas afirmações de frescor). Nada persistido seria mais verdadeiro do que a observação que o gerou.
- **Só observação *stale* gera pulo**. Observação fresca nunca bloqueia o uso — espelho fresco segue atendendo. A "cura" acontece naturalmente quando a janela expira: re-probe → resposta fresca substitui a observação anterior.
- **Pulo ≠ falha de infraestrutura**: tudo pulado → resultado `available + stale + vazio` (ciclo grava o adiamento `overpass_stale_data`, não terminal); se há falha real misturada (`http_*`, timeout, tipo de conteúdo) o motivo continua `unavailable(...)` — a infraestrutura segue adiável/retryável como sempre.
- **Janela < 1 min desliga o pulo** (sempre sonda): alavanca de operador se algum dia o comportamento sticky precisar ser neutralizado sem deploy.
- **Custo zero onde não há custo**: o cheque antes da reserva segue a filosofia da Sprint 65 — decisão informada por estado já pago não paga de novo. O teto continua sendo o contrato de cortesia com o Nominatim; o Overpass deixa de consumir unidade dele nos adiamentos puramente informativos.
- **Observação apenas de respostas bem-sucedidas** — mesma regra do cache de consulta da Sprint 66: falha de transporte não diz nada sobre a idade do dado.

## Validação

- Compilação Release sem avisos nem erros (`--no-incremental`).
- Suítes completas: **507 testes unitários aprovados** (9 novos em `Sprint67OverpassFreshnessTests`: early-return sem rede, dois espelhos cobertos em duas sondas e terceiro custo zero, espelho velho pulado enquanto o fresco atende, expiração da janela re-sondando e re-coloqueando o sticky, cura por resposta fresca, idade ilegível colando conservadora, janela zerada desligando o pulo, pulo + falha real continuando `unavailable(...)`, parse ISO/epoch/ausente de `ReadBaseTimestamp`) e **238 testes de integração aprovados** mais 1 preexistente ignorado — sem alteração nesta Sprint (nenhuma migração).
- Ao vivo (evidência em `artifacts/sprint67-live-evidence.txt`): fila com 87 `overpass_stale_data` e teto do dia UTC **301/300 esgotado**; três ciclos do novo binário processaram 3 fichas e o teto ficou **invariante em 301** (reserva atômica `granted<300` rejeitou todas as tentativas novas, `requested` subiu para 357). Com o teto esgotado nenhum probe financiado foi possível hoje — as observações sticky acendem a partir de 00:00 UTC de 24/10, com no máximo 1 probe por espelho por janela de 60 min e zero unidade/HTTP em todos os demais adiamentos. Leitura honesta registrada no arquivo de evidência.

## Operação

Worker parado durante compilação/testes; geração de observação 44312/39600 com ciclo temporariamente acelerado para 2 min via variável de ambiente (somente daquele processo) e geração final **50192/34232** com os padrões do `appsettings.json` (ciclo 10 min) — processo único verificado e heartbeat `running`. Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado. Nenhuma coordenada nova aplicada no período.

## Encerramento operacional

A informação "este espelho está velho" agora é **descoberta uma vez por janela e reutilizada**: dentro dela, todo adiamento por espelho antigo custa zero unidade do teto e zero requisição, e espelhos velhos saem do caminho do failover para dar lugar a espelhos frescos. A fila de 87 `overpass_stale_data` deixa de pagar para reencontrar o mesmo veredicto — e o teto diário volta a servir somente o que ainda precisa ser perguntado.
