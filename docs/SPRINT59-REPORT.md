# Sprint 59 — painel de gestão da geocodificação autônoma

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

A Sprint 58 criou a geocodificação autônoma e manteve observação por CLI/logs/banco dentro do escopo: "Não foi adicionado painel administrativo nesta Sprint". Com ~17 mil itens enfileirados e rejeições que ficam terminais na fila, a operação diária precisa ver e intervir por interface autenticada, com o mesmo rigor dos demais painéis administrativos (papel de moderador e segundo fator).

## O que mudou

Painel `/admin/geocodificacao`, protegido pela política `RestaurantModerator` (mesma de `/admin/crawler` e `/admin/pesquisa-culinaria`):

- Contagens reais da fila, lidas do mesmo contrato usado pelo Worker (`IGeocodingQueue`): pendente, em processamento, com coordenada aplicada, falhou após 3 tentativas, rejeitada e requisições ao Nominatim usadas hoje.
- Lista da fila paginada (25 por página), filtrável por estado e por nome da ficha, exibindo o último motivo estável, tentativas, precisão aplicada, objeto OSM e última atualização em UTC, com link direto para a ficha pública.
- Ações humanas, todas com anti-forgery e auditadas em `audit_events`:
  - **Re-enfileirar** (por ficha): só aceita estado terminal (`skipped`/`failed`) enquanto a ficha segue sem coordenada; repetir a ação não duplica nem muda nada.
  - **Re-enfileirar rejeições antigas** (lote): devolve apenas itens com atualização há 7 dias ou mais — o mapa do OSM melhora ao longo de semanas —; por ser finita por construção, dispensa tela de confirmação, e o ritmo continua limitado pelo teto diário compartilhado.
- Item de navegação na barra lateral do layout administrativo.

## Decisões

- A camada Web não duplica SQL: `ListTasksAsync`, `RequeueAsync` e `RequeueStaleTerminalsAsync` entraram em `IGeocodingQueue` (Infraestrutura), então painel e Worker leem e escrevem o mesmo contrato.
- O re-enfileiramento zera tentativas e lease e preserva o último motivo como histórico da linha. O claim continua exigindo `latitude IS NULL` no banco, então coordenada recebida por outro caminho torna o item não elegível antes mesmo de ser processado.
- Auditoria segue o padrão do projeto: `audit_events` com ator, ação (`geocoding.requeue` / `geocoding.requeue_stale`), entidade e metadados JSON (estado anterior ou contagem do lote).
- Re-enfileirar é ação humana deliberada: nenhum cronômetro no Worker volta rejeição sozinha.
- Sem nova migração: reutiliza `geocoding_tasks` (047) e `audit_events` (003).

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **442 testes unitários aprovados** e **229 testes de integração aprovados**, mais um teste preexistente ignorado. Os três testes novos (`Sprint59GeocodingAdminTests`) cobrem: acesso autenticado com MFA exibindo a fila, re-enfileiramento individual idempotente com auditoria única, não moderador (anunciante) barrado antes do painel e o corte de 7 dias devolvendo apenas terminais antigos.
- Ao vivo: servidor Development subido; `/admin/geocodificacao` sem sessão respondeu 302 para `/admin/login?ReturnUrl=/admin/geocodificacao` com a política logada (papéis `restaurant_moderator|platform_admin` e claim `amr=mfa`); Home respondeu 200. Evidência em `artifacts/sprint59-live-route.log`.

## Operação

O Worker local ficou parado durante compilação e testes e volta a operar via `scripts/windows/run-local-worker.ps1`. Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails.

## Encerramento operacional

Worker reiniciado e processo confirmado ativo (PID do estado em `artifacts/worker-local/state.json`), com heartbeat recente logo após a última compilação (`artifacts/sprint59-worker-health.json`). O primeiro ciclo periódico de geocodificação segue o intervalo configurado e o teto diário.
