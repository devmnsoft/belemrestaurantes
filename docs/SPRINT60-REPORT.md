# Sprint 60 — revisão humana das coordenadas aplicadas pelo OSM

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

As Sprints 58/59 fecharam o caminho de aplicar e acompanhar coordenadas, mas deixaram uma lacuna operacional: se o OSM errou e uma coordenada rooftop foi aplicada automaticamente, não havia caminho na interface para desfazê-la — só SQL direta contra `restaurants`. A escrita automática precisa do seu escape humano: rever o que foi aplicado, com rastro e possibilidade de nova tentativa quando houver o que conferir de novo (endereço corrigido na ficha, mapa do OSM melhorado).

## O que mudou

- **Reverter coordenada** no painel `/admin/geocodificacao`: botão "Reverter coordenada" nas linhas `completed` da fila (que por definição só existem quando o Worker aplicou origem OpenStreetMap/Nominatim). A ação:
  - remove `latitude`, `longitude`, `coordinate_precision` e `coordinate_source` da ficha — **somente** quando a origem atual é reconhecida como `OpenStreetMap via Nominatim (...)` e a ficha não tem proteção Tier 1 (dono cadastrado ou claim aprovado). Ficha protegida, sem coordenada ou de outra origem sai intacta;
  - devolve a linha da fila ao estado `skipped` com motivo estável `reverted_by_admin` e prazo 2099 — sem nova tentativa imediata, para não consumir cota repetindo a mesma consulta que acabou de ser rejeitada;
  - grava o histórico do atributo `coordinate` (valor antigo → "(removido por revisão administrativa)", origem `admin_review`, confiança 1.000) e auditoria `geocoding.revert` em `audit_events` com coordenada e origem removidas.
  - Para nova tentativa, o operador usa "Re-enfileirar" (botão que já aparece em terminais) quando fizer sentido; o lote "Re-enfileirar rejeições antigas" cobre o resto após 7 dias.
- **CLI pontual no Worker** `--revert-geocode <id>`: mesma ação para operação/mantença fora do navegador (mesmo histórico, mesma auditoria, ator `worker-cli`), imprime JSON do resultado.
- Correlato: a metadados de auditoria de revert passou a formatar coordenadas com cultura invariante (ponto decimal), consistente com o histórico.

## Decisões

- A ação é um contrato único em `IGeocodingQueue` (`RevertAppliedCoordinatesAsync`): painel e CLI chamam o mesmo método transacional (ficha + fila + auditoria), evitando divergência entre dois caminhos de escrita.
- O guard de origem fica no `UPDATE` (`coordinate_source LIKE 'OpenStreetMap via Nominatim%'`) além da checagem inicial: nenhuma escrita depende apenas do estado anterior, e Tier 1 continua intocável por `NOT EXISTS` nos donos e claims aprovados.
- Reverter **não** re-enfileira imediatamente de propósito: rever porque o OSM está errado e rodar de novo agora custaria a consulta e traria o mesmo objeto. A rejeição revisada espera intervenção humana ou o lote de antigas.
- O histórico é gravado fora da transação da fila (mesma convenção já usada pelo Worker no apply): atributo removido nunca "desfaz" o registro anterior — o rastro completo (aplicar → reverter → reaplicar) permanece legível em `restaurant_attribute_history`.
- Sem nova migração: reutiliza `restaurants`, `geocoding_tasks` (047), `restaurant_attribute_history` e `audit_events` (003).

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **442 testes unitários aprovados** e **231 testes de integração aprovados**, mais um teste preexistente ignorado. Os dois testes novos (`Sprint60CoordinateReviewTests`) cobrem, sobre HTTP real com MFA: revert de coordenada OSM aplicada limpando a ficha, devolvendo a fila a `skipped/reverted_by_admin` com auditoria única e histórico do atributo; e revert de coordenada de outra origem mudando nada (ficha intacta, sem auditoria nem histórico).
- Ao vivo, round-trip completo na ficha real 6340 (LA BOCA): `--revert-geocode 6340` removeu a rooftop (−1.4545559/−48.4741902) deixando NULL na ficha e `skipped/reverted_by_admin` na fila, com `geocoding.revert` auditado e histórico `admin_review`; re-enfileirada, o ciclo `--geocode-pending` reencontrou o mesmo objeto OSM (13072894) e reaplicou a rooftop. Estado final restaurado. Evidência em `artifacts/sprint60-revert-demo.txt`.

## Operação

O Worker local ficou parado durante compilação e testes e volta a operar via `scripts/windows/run-local-worker.ps1`. As requisições ao Nominatim consumidas pelas demonstrações entram no teto diário compartilhado normalmente. Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails.

## Encerramento operacional

Worker reiniciado após as últimas compilações, com estado e heartbeat confirmados em `artifacts/worker-local/state.json`. A rejeição revisada de qualquer ficha só volta à fila por ação humana (botão ou lote), conforme projetado.
