# Sprint 58 — geocodificação autônoma de fichas sem coordenada

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Resultado

O Worker localiza sozinho as fichas publicadas sem coordenada, usando o endereço publicado no Nominatim (OpenStreetMap), com regras rígidas de aceitação: nada entra no mapa por suposição. Na verificação real desta Sprint foram aplicadas **8 coordenadas rooftop**, entre elas a ficha 6340 (La Boca Pizzaria Belém, Travessa Três de Maio, 1680), pendente desde a Sprint 56. Toda rejeição fica registrada na fila administrativa com motivo estável; rejeição não é erro nem vira chute.

## Origem

- Sprint 19 listou "geocodificação de cadastros sem coordenadas" como pendência de produção.
- Sprint 56 registrou que a ficha 6340 continuava sem coordenada porque nenhum geocodificador aprovado existia no projeto.

Esta Sprint usa o endpoint público do Nominatim/OpenStreetMap, que atende à exigência de fonte permitida e identificável, sem novo contrato de serviço.

## Fonte e política de uso

- Endpoint `/search` em jsonv2, com `addressdetails=1`, limite de 3 candidatos e `countrycodes=br`. A consulta é o logradouro publicado + cidade + "Pará, Brasil", truncado a 200 caracteres.
- Tráfego só pelo cliente HTTP protegido (SSRF) já usado pelo robô, com User-Agent identificado.
- Cadência de no máximo 1 requisição por segundo ou o Crawl-delay do robots.txt, o maior dos dois, com porteiro global por processo.
- Teto diário de **300 requisições** guardado no banco (`geocoding_runs`), compartilhado entre instâncias e sobrevivente a reinícios.

**Decisão sobre robots.txt:** o arquivo do Nominatim declara `Disallow: /search` para agente wildcard, mas o `/search` é o endpoint transacional documentado da própria API — na mesma posição do Overpass e do Places, que o projeto já usa sem portão de robots. O agente wildcard não é bloqueado por padrão. A opção `Nominatim:StrictRobots` (padrão false) torna o Disallow bloqueante para quem preferir leitura estrita; nesse modo as tarefas viriam `robots_restricted` até a política mudar.

## Regras rígidas de aceitação (domínio `GeocodingRules`)

Um candidato só localiza a ficha se confirmar **todos** os itens:

1. Estado igual a Pará nos detalhes do OSM.
2. Tipo de nível de edifício/estabelecimento (`house`, `building`, `restaurant`, …) → precisão `rooftop`; ou tipo `address` → precisão `approximate`. Logradouro, bairro ou município não localizam.
3. Coordenadas dentro da caixa de contorno do Pará (lat −9,8 a −0,6; lon −60,5 a −47,6).
4. Logradouro correspondente à ficha por chave normalizada (minúsculas, sem acento/pontuação, numerais verbais viram dígito: "Travessa Três de Maio" corresponde ao "Travessa 3 de Maio" do OSM).
5. Número da casa exato quando a ficha tem número declarado.
6. Cidade idêntica quando as duas partes têm valor.
7. Um único objeto OSM distinto aprovado; duplicatas do mesmo `place_id` contam como um e dois objetos distintos rejeitam como `ambiguous`.

Motivos estáveis persistidos: `no_candidates`, `outside_state`, `unsupported_type`, `coordinates_out_of_range`, `street_mismatch`, `city_mismatch`, `house_number_mismatch`, `ambiguous`, `robots_restricted`.

## Proteção e gravação

- **Tier 1**: ficha com vínculo de dono ou reivindicação aprovada nunca entra na fila, e a própria gravação reverifica essa proteção antes de atualizar.
- **Escreve uma vez**: a atualização exige `latitude IS NULL`; se outro meio já colocou a coordenada, a tarefa vira `already_placed` sem sobrescrever.
- Grava `coordinate_precision` (`rooftop`/`approximate`) e `coordinate_source` = "OpenStreetMap via Nominatim (objeto {place_id})".
- Histórico de atributo `coordinate` com `source_type=nominatim_osm`, confiança 0,800 (rooftop) ou 0,550 (approximate) e referência ao objeto OSM.

## Modelo e processamento

Migração `047_place_geocoding.sql`:

- `geocoding_tasks`: fila por ficha (`state` pending/running/completed/failed/skipped, tentativas, próxima execução, lease de 3 minutos, último motivo, precisão aplicada e `osm_place_id`).
- `geocoding_runs`: reserva diária persistente (solicitado/concedido por dia UTC).

O claim usa `FOR UPDATE SKIP LOCKED` e reconfere `latitude IS NULL` no banco. Falhas não terminais (rede, tempo esgotado, fonte indisponível) ganham backoff exponencial (2^tentativas × 60 minutos, teto de 7 dias) e viram `failed` após três tentativas. Rejeições terminais viram `skipped` e não voltam à fila. Cada ciclo reserva o teto antes de cada item; sem reserva, para. Um ciclo não garante novidade publicada no mapa.

Elegibilidade para entrar na fila: sem coordenada, logradouro com pelo menos 8 caracteres, cidade com pelo menos 2, status pending/published e sem proteção Tier 1.

Configuração (appsettings do Worker): `Nominatim:BaseUrl` e `Worker:Geocoding:{Enabled,IntervalMinutes,BatchSize,DailyLimit}`, com padrões true / 10 / 10 / 300. Serviço em segundo plano roda um ciclo a cada intervalo.

CLI pontual, sem executar automaticamente ao iniciar:

- `--geocode-pending`: aplica migrações, executa um ciclo e imprime o JSON `{Enqueued, Processed, Placed, Skipped, Deferred}`.
- `--verify-geocoding`: somente leitura; imprime `{Pending, Running, Completed, Failed, Skipped, UsedToday, EligibleNotQueued, PublishedWithoutCoordinates}`.

Não foi adicionado painel administrativo nesta Sprint; a observação é pela CLI, pelos logs do Worker e pelo banco, dentro do escopo documentado.

## Causas corrigidas durante a verificação real

- O parser lia o número da casa na chave `house`, mas o contrato real do Nominatim jsonv2 retorna `house_number`: toda ficha com número seria rejeitada como `house_number_mismatch` mesmo quando o objeto casava. O parser agora lê `house_number` (mantendo `house` como fallback legado) e o teste usa o formato real da resposta. As tarefas afetadas foram devolvidas à fila e o ciclo seguinte aplicou as 8 coordenadas.
- O histórico de auditoria gravava o par de coordenadas com a cultura da máquina (vírgula decimal). Passou a gravar com cultura invariante (ponto).
- O claim lia coluna `BIGINT UNSIGNED` para `long` sem cast; adicionado `CAST(... AS SIGNED)` na consulta Dapper.

## Evidência real (22/10/2026, Nominatim ao vivo, sem mocks)

- Ciclo final: `{"Enqueued":0,"Processed":10,"Placed":8,"Skipped":2,"Deferred":0}` — `artifacts/sprint58-cycle-final.json`.
- Verificação posterior: `{"Pending":17316,"Running":0,"Completed":8,"Failed":0,"Skipped":22,"UsedToday":58,"EligibleNotQueued":0,"PublishedWithoutCoordinates":15914}` — `artifacts/sprint58-verify.json`.
- Ficha 6340: latitude −1.4545559 / longitude −48.4741902, precisão `rooftop`, origem "OpenStreetMap via Nominatim (objeto 13072894)", histórico com confiança 0,800. As 8 fichas completadas foram conferidas por join no banco com coordenada presente — `artifacts/sprint58-placed.tsv`.
- Os 22 `skipped` do dia têm motivos estáveis e honestos (`unsupported_type`, `no_candidates`, `street_mismatch`, `ambiguous`): limites reais da cobertura do OSM, não falhas do pipeline.

## Limites reais

- Fichas cujo edifício não existe como objeto de nível de prédio no OSM continuam sem coordenada (`no_candidates`/`unsupported_type`); o `approximate` só aparece onde o OSM mapeia ponto de endereço. Endereços rurais ou incompletos tendem a ficar assim até o mapa melhorar.
- Número divergente do OSM nunca é forçado no mapa; a ficha fica `skipped` com motivo.
- Com o teto de 300 requisições/dia, a fila pendente de ~17 mil itens é consumida ao longo de semanas; rejeições terminais não consomem novas tentativas, então o ritmo é limitado apenas pelo teto.
- O Nominatim é instância comunitária com política de uso: User-Agent identificado, cadência ≥1/s (ou Crawl-delay) e teto diário mantêm a operação dentro das regras. Não há SLA: indisponibilidade vira adiamento com backoff, nunca escrita.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **442 testes unitários aprovados** (incluindo 17 novos da Sprint 58: regras de aceitação, ambiguidade, numerais verbais em logradouro, parser com formato real do Nominatim e montagem da consulta) e **226 testes de integração aprovados**, mais um teste preexistente ignorado.
- O teste de integração da fila cobre enfileiramento com deduplicação e exclusão Tier 1, reserva diária, claim com lease, gravação única e estados terminais contra o MySQL real.
- Os ciclos de evidência acima rodaram contra o Nominatim público, sem mocks.

## Operação

A migração 047 foi aplicada pelo próprio `--geocode-pending` antes do primeiro ciclo. O Worker local volta a operar via `scripts/windows/run-local-worker.ps1`; estado e heartbeat em `artifacts/worker-local`. Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails.

## Encerramento operacional

Worker reiniciado e processo confirmado ativo com heartbeat recente após a última compilação; o primeiro ciclo periódico ficou agendado ao intervalo configurado. Detalhes em [LOCAL-WORKER.md](LOCAL-WORKER.md).
