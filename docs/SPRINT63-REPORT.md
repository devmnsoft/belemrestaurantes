# Sprint 63 — visibilidade e retomada por motivo de rejeição da fila de geocodificação

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

A consulta direta ao banco mostrou onde a fila de ~17 mil pendentes ia travar: das **227 linhas terminais**, **155 (68%) eram `unsupported_type`** — exatamente o caso que o fallback Overpass da Sprint 62 resolve, mas rejeitadas *antes* do fallback existir, e por isso presas em estado terminal (a regra de 7 dias do lote antigo não cobria linhas recentes, e não havia forma de ver essa distribuição sem SQL nem de devolver uma categoria inteira à fila agora que a capacidade mudava).

## O que mudou

- **Resumo da fila** (`IGeocodingServices`/`MySqlGeocodingQueue`): `GeocodingSummary` ganhou `SkipReasons` — distribuição das linhas terminais rejeitadas pelo último motivo (`GROUP BY last_status`, em ordem decrescente, `NULL` exibido como "(sem motivo)"). Flui automaticamente para o painel e para o JSON de `--verify-geocoding`.
- **Fila** (`IGeocodingQueue.RequeueByReasonAsync`): devolve à fila (`pending`, `attempts=0`, lease limpa, `next_run_at=agora`) as linhas `skipped`/`failed` cujo último motivo é exatamente o informado e cuja ficha ainda não tem coordenada — os mesmos guards semânticos do lote antigo (terminal + `latitude IS NULL`). Motivo fora do formato `[a-z][a-z0-9_]*` lança `ArgumentException`; auditoria `geocoding.requeue_by_reason` com motivo e quantidade quando algo volta.
- **Painel** (`/admin/geocodificacao`): nova seção "Motivos de rejeição nas linhas terminais" com um cartão por motivo (badge + quantidade + botão **Re-enfileirar**), via nova rota humana `POST /admin/geocodificacao/reenfileirar-por-motivo` (moderador + MFA, antiforgery). O endpoint só recebe motivos como os exibidos na própria seção — nada de texto livre arbitrário.
- Sem nova migração, sem nova flag de CLI (o `--verify-geocoding` herda `SkipReasons`); o par equivalente de cada ação segue sendo painel + CLI onde já existia.

## Decisões

- **Retomar não consome cota na hora**: re-enfileirar é só banco; cada ficha volta a ocupar unidades do teto diário compartilhado quando o ciclo efetivamente consultar (orçamento por chamada externa, como desde a Sprint 58). A ação permite decidir *qual categoria* merece o orçamento de amanhã.
- **Motivo é token estável**: a validação por formato rejeita entrada antes de tocar no banco, e o painel só oferece motivos existentes — o operador age sobre o que viu, nunca sobre um valor inventado.
- `last_status` **não é limpa** ao re-enfileirar (mesma convenção do re-enfileiramento por ficha e do lote de 7 dias): a fila continua mostrando *por que* a ficha esteve parada até a próxima decisão do ciclo sobrescrever.
- Linhas `failed` também são elegíveis, com a mesma semântica do lote antigo; a regra de 7 dias permanece como mecanismo periódico genérico — a ação por motivo responde a outra pergunta: "a capacidade mudou para esta categoria, repito agora".

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **469 testes unitários aprovados** e **235 testes de integração aprovados** (2 novos em `Sprint63SkipReasonTests`), mais um preexistente ignorado. Os novos exercem HTTP real com MFA sobre o banco local compartilhado, com restauração integral em `finally`: (1) POST `reenfileirar-por-motivo` devolve à fila apenas as linhas daquele motivo (`pending`, `attempts=0`) com auditoria única contendo motivo/quantidade, deixando o outro motivo intacto, confere a seção do painel e a exposição do resumo; (2) motivo bem formatado sem linhas correspondentes e motivo fora do formato não alteram nada nem geram auditoria.
- Ao vivo (evidência em `artifacts/sprint63-live-evidence.txt`): `--verify-geocoding` imprimiu `"SkipReasons":{"no_candidates":65,"ambiguous":4,"street_mismatch":3}`, com o painel e a rota conferidos (302 → `/admin/login?ReturnUrl=/admin/geocodificacao` com a política logada; home 200). O fluxo de retomada rodou de ponta a ponta sobre o banco real: as **155 linhas terminais `unsupported_type` voltaram a `pending` com `attempts=0`** (confirmando no banco), deixando `Skipped` em 72 — elas passam pelo fallback Overpass da Sprint 62 a partir do próximo ciclo com cota fresca. A auditoria `geocoding.requeue_by_reason` foi confirmada em teste e limpa pelo `finally` da suíte, como manda a convenção dos testes que compartilham o banco.

## Operação

O Worker local ficou parado durante compilação e testes e voltou via `scripts/windows/run-local-worker.ps1` (supervisor 28520, worker PID 40672, heartbeat confirmado). Atenção honesta de orçamento: as 155 fichas retomadas consomem 1 unidade Nominatim cada (mais 1 de Overpass quando houver âncora) contra o teto de 300/dia — o lote ocupa, sozinho, todo o orçamento de amanhã ou um pouco de dois dias, adiantando a fila de ~17 mil pendentes. Nada mais mudou: mesmas senhas, MFA, cotas e endereços.

## Encerramento operacional

Fila agora com 17.242 pendentes e 72 rejeições terminais (65 `no_candidates`, 4 `ambiguous`, 3 `street_mismatch`). As 155 fichas `unsupported_type` aguardam o próximo ciclo diário para passarem pela primeira vez pelo caminho Overpass; os 3 adiamentos `overpass_stale_data` seguem em backoff. Nenhuma coordenada publicada mudou nesta Sprint.
