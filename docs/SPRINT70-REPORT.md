# Sprint 70 — o pipeline de diagnóstico prova a si mesmo: `ci-log` publicada de verdade no `main` (e com o bug que impedia isso desde o primeiro dia, encontrado e corrigido)

Execução local em 23/10/2026. Push em `main` com duas janelas temporariamente vermelhas, documentadas abaixo. Nenhum segredo adicionado ou versionado.

## Origem

A Sprint de `61bd764` ("publish integration failure diagnostic as annotated tag ci-log") instalou o mecanismo: quando a suíte de integração falha no CI, o job monta um diagnóstico (testes `[FAIL]`, primeiras mensagens de erro, rastro do log) e publica como **tag anotada `ci-log`** apontando para o commit ofensor — antes de falhar. Mas o mecanismo **nunca tinha sido executado**: o CI só ficava verde. Um diagnóstico que nunca saiu é um plano, não um fato. Esta Sprint existe para transformá-lo em fato, e colou uma corroboração ao vivo do frescor sticky da Sprint 67 na mesma janela operacional.

## O método: falha controlada e ref-aware

- **Sonda temporária** (`Sprint70CiLogProbeTests`, removida no commit seguinte): um teste que falha com mensagem identificável **apenas quando `GITHUB_REF=refs/heads/*`** (push para branch). Em tag, PR e execução local ele passa. Consequência exata que precisávamos: o mesmo SHA pode primeiro **aquecer o check obrigatório verde via tag** (`s70a-warmup`) e depois assumir o **vermelho controlado ao chegar em `main`** — sem tocar na proteção de branch nem no workflow.
- Janela vermelha aceita e registrada: `main` ficou vermelha em dois intervalos (~10 min em `b2016b4`; ~3 min em `4ac83df`). Nenhuma publicação/operacão dependente do CI ocorreu no intervalo; o verde voltou no encerramento.

## O que a prova revelou: o mecanismo tinha bug latente

A primeira rodada (`b2016b4`) falhou **exatamente onde deviamos** — passo "Integration Tests" vermelho, sonda identificada — **mas a tag `ci-log` não saiu**. Diagnóstico (logs completos exigem admin no repositório; as conclusões por passo vieram da API pública do job): o shell padrão dos `run` do Actions em Linux é `bash -e` (errexit). Com errexit ativo, a linha `dotnet test ...` vermelha **encerra o script antes de chegar no `rc=$?`** — todo o bloco de diagnóstico e publicação era código morto inalcançável. Nunca tinha aparecido porque o CI nunca tinha ficado vermelho antes.

**Correção mínima** (`4ac83df`): `rc=0; dotnet test ... > ci-integration.log 2>&1 || rc=$?` — três linhas, com comentário explicando o porquê, sem alterar nenhum outro comportamento do job. Segunda rodada: push para `main`, e **~180 s depois a tag anotada `ci-log` estava publicada**, apontando para o commit ofensor, assinada pelo `github-actions[bot]`.

## A evidência (resumo — o verbatim está em `artifacts/sprint70-live-evidence.txt`)

- Anotação da tag: nome do teste falho, a mensagem controlada da sonda, stack trace com `file:line` no runner, o rastro do log e os totais da suíte — **Total 242 / Passed 240 / Failed 1 (a sonda) / Skipped 1** (a verificação opcional de credenciais já documentada). Tudo o mais verde em MySQL 8.4 docker fresco.
- Nota cruzada valiosa: nessa mesma execução CI, o `CuiaAiEndpointTests.Ask_WithValidToken_ReturnsJson` **passou** — no banco fresco e sem o LLM gratuito local respondendo saudações, confirmando a classificação "dependência ambiental" dada na Sprint 69 à mesma falha local.
- Encerramento limpo: a tag `ci-log` foi capturada verbatim para o artifact e depois removida do remoto, restaurando o estado "sem tag = sem última falha conhecida"; as tags de aquecimento foram todas removidas (remoto + local).

## Corroboração colateral: o sticky da Sprint 67 num processo longevo

Durante a parada do Worker para a Sprint, um lote one-shot (`DailyLimit=500`, `BatchSize=20`, processo novo e longevo o suficiente) processou 20 fichas reais em 175 s — 5 coordenadas rooftop aplicadas, 3 adiamentos Overpass. A sequência prova o pulo sticky dentro da janela:

1. Ficha 1060 (14:24:01): primeira consulta sondea os três espelhos; osm.ch responde velho → linha `overpass_mirror_freshness` gravada às 14:24:01.538909; adia em `overpass_stale_data`.
2. Fichas 1065/1066 (14:25:04 / 14:25:27): consultas seguintes **pularam o osm.ch** — o motivo `unavailable(0:http_406|1:http_0)` lista só de/kumi — e a linha do banco **não se moveu** (sonda fresca teria atualizado o `observed_at`).
3. Limite honesto: de/kumi falharam com 406/timeout (infraestrutura, não idade), então o atalho zero-HTTP "todos confirmados velhos" não pôde disparar hoje. Prova completa dele continua pendente de janela com cota renovada e espelhos respondendo — registrado, não forçado.

## Validação

- Sem mudança em `src/` nesta Sprint (workflow + teste + docs): a base de compilação continua a da Sprint 69 (Release 0/0 `--no-incremental`, 521 unit, 239+1 integração local).
- Sonda verificada nos dois modos localmente: verde com `GITHUB_REF` ausente (execução isolada aprovada); o modo vermelho só existe no contexto do Actions, exercitado duas vezes (a segunda, produtiva).
- Evidência ao vivo completa e datada em `artifacts/sprint70-live-evidence.txt`, incluindo a anotação verbatim da tag e os SHAs/runs de cada etapa (todos consultáveis anonimamente no repositório público).

## Operação

Worker parado durante a Sprint (~14:2x–15:0x UTC) e reiniciado em seguida com o mesmo binário da Sprint 69 (nenhum `src/` alterado). Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado. Decisão registrada: a tag `ci-log` segue sendo **transitória por contrato** (force-updated a cada falha); mantê-la após o verde restauraria falsamente um "último estado vermelho".

## Encerramento operacional

O diagnóstico de falha deixou de ser desenho no papel: uma falha real no `main` produziu, em minutos, uma tag que qualquer operador consegue ler sem abrir console — o quê falhou, a mensagem exata, o arquivo e a linha, e como a suíte terminou. E a prova custou o melhor que podia custar: o bug que garantia que ela nunca funcionasse. Na mesma janela, o frescor sticky mostrou-se vivo num processo de verdade, pulando o espelho velho sem que ninguém pedisse.
