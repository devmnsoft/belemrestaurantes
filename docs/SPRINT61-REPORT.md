# Sprint 61 — revisão e ajuste humano das coordenadas aplicadas

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

Sprint 58 aplica coordenadas automaticamente; Sprint 59 dá visibilidade à fila; Sprint 60 permite desfazer (reverter) o que o OSM aplicou. Faltava a outra metade do controle de qualidade: quando o revisor **sabe** onde o lugar está (conferiu no mapa, o dono informou), ele precisava apenas apagar e torcer para o OSM acertar na próxima tentativa — ou editar via SQL. Agora a linha `completed` do painel mostra a coordenada real com link direto para conferir o ponto no OpenStreetMap e oferece ajuste in-place com histórico e auditoria.

## O que mudou

- **Coluna "Coordenada (ficha)"** na lista do painel `/admin/geocodificacao`: par lat/lng atual do banco (não só o que a fila guarda), com link `https://www.openstreetmap.org/?mlat=…&mlon=…#map=18/…` para conferir visualmente se o pino caiu no lugar certo; o ID do objeto OSM fica como legenda da linha.
- **Ajustar** (por linha `completed`): formulário inline com latitude/longitude pré-preenchidos com o valor atual e precisão declarada (`approximate` por padrão, `rooftop` opcional). A ação:
  - valida numérica (aceita vírgula ou ponto decimal) e exige o par dentro da **mesma caixa de contorno do Pará** usada pelas regras de aceitação (`GeocodingRules.IsCoordinateInRange`); fora da caixa, nada é tocado;
  - substitui o par **somente** em ficha com origem gerida por este ciclo (`OpenStreetMap via Nominatim (...)` ou `Ajuste administrativo (revisão humana)`) e sem proteção Tier 1 — guard repetido no pré-guard C# e no próprio `UPDATE`;
  - grava histórico do atributo `coordinate` (par antigo → novo, origem `admin_review`, referência com a origem substituída, confiança 1.000) e auditoria `geocoding.adjust` em `audit_events` com pares antigo/novo e precisão;
  - deixa a linha da fila como `completed/adjusted_by_admin`: o claim do Worker exige `latitude IS NULL`, então nenhuma tentativa automática sobrescreve o ajuste.
- **Guard expandido para reverter (Sprint 60)**: coordenada já ajustada manualmente também pode ser revertida (mesma proteção Tier 1). O ciclo humano fica completo: aplicar → ajustar → reverter → re-enfileirar.
- **Cartões de qualidade** no topo do painel: `Rooftop nas fichas` e `Approximate nas fichas`, contados no banco pelas origens geridas por este ciclo (aplicada pelo OSM ou ajustada por revisão), pela precisão declarada.
- Contrato: `AdjustAppliedCoordinatesAsync` + `CoordinateAdjustResult` em `IGeocodingQueue`; `GeocodingTaskRow` ganha `Latitude/Longitude`; `GeocodingSummary` ganha `RooftopApplied/ApproximateApplied`. Sem nova migração.

## Decisões

- Precisão declarada pelo revisor, não inferida: o sistema não sabe sozinho se um par digitado é rooftop; a opção vira metadado auditável e continua honrando a regra da Sprint 56 (só rooftop trata-se como pino exato).
- O ajuste aceita **refazer** (origem administrativa já presente): erro de digitação no ajuste não exige reverter antes de corrigir.
- Re-enfileirar não aparece em linha ajustada (`completed`): a ficha tem coordenada; para voltar ao processo, o caminho é Reverter (que agora aceita origem ajustada) e depois Re-enfileirar.
- Mesma separação das Sprints anteriores: SQL só em Infraestrutura, validação de domínio (`IsCoordinateInRange`) reutilizada do `GeocodingRules`, Web orquestra e grava o histórico do atributo fora da transação (mesma convenção do apply automático).
- CLI: `--revert-geocode <id>` (Sprint 60) herdou o guard expandido automaticamente, pois chama o mesmo método.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **442 testes unitários aprovados** e **233 testes de integração aprovados**, mais um teste preexistente ignorado. Os dois testes novos (`Sprint61CoordinateAdjustTests`) cobrem, sobre HTTP real com MFA: ajuste de origem OSM aceito (ficha atualizada, origem administrativa, linha `completed/adjusted_by_admin`, auditoria única) seguido de 2º ajuste sobre origem administrativa (guard expandido) com o histórico preservando o par e a origem originais; e ajuste fora da caixa do Pará / não numérico mudando nada (ficha intacta, sem auditoria nem histórico).
- Ao vivo: `--verify-geocoding` respondeu `{"Pending":17179,...,"RooftopApplied":22,"ApproximateApplied":0}` — todas as 22 fichas com coordenada aplicada são rooftop, zero approximate. Evidência em `artifacts/sprint61-verify.json`.

## Operação

O Worker local ficou parado durante compilação e testes e volta a operar via `scripts/windows/run-local-worker.ps1`. Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails. As 202 requisições do teto diário são as dos ciclos regulares de hoje.

## Encerramento operacional

Worker reiniciado após a última compilação, com estado e heartbeat confirmados em `artifacts/worker-local/state.json`. Nenhuma ficha real teve coordenada alterada nesta Sprint (os ajustes dos testes usam fixtures restauradas no finally).
