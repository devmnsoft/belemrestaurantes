# Sprint 66 — cache de consulta: respostas reais do Nominatim de hoje rejogadas sem cota

Execução local em 23/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

Com o dreno em ritmo normal, duas medições da fila expuseram o desperdício que restava:

- **68 fichas pendentes presas em `overpass_stale_data`** (crescidas com o lote de `unsupported_type` da Sprint 63 passando pelo fallback Overpass sobre espelhos antigos). Cada uma delas, a cada novo ciclo de retry, **pagava ao Nominatim pela mesma consulta** — e o Overpass voltava a dar o mesmo veredicto de espelho antigo.
- **3.309 fichas pendentes vivem em pares exatos (endereço, cidade) duplicados** (~2.200 consultas redundantes certas), e o segundo olhar da Sprint 64 refaz a busca só-do-logradouro de cada ficha mesmo quando outra ficha da mesma rua já obteve a resposta daquela rua naquele dia.

O teto diário é por dia UTC (`UTC_DATE()` no banco) e o mapa do OSM não muda dentro do dia. Logo: **uma resposta real servida hoje para uma consulta é rejogável dentro do dia UTC sem perda de verdade** — e cada replay é uma unidade de cortesia devolvida.

## O que mudou

- **Migração 049** (`049_query_cache.sql`): `geocoding_query_cache(query_key CHAR(64), day DATE, payload_json MEDIUMTEXT, hits INT, created_at)` com PK `(query_key, day)`.
- **Chave byte-fiel**: SHA-256 da **exata string de consulta** que o `/search` recebe — o mesmo `NominatimGeocodingSource.BuildQuery(address, city)` usado na requisição. Rejeitar do cache é sempre rejogar aquilo que a fonte mesma respondeu àquela mesma URL.
- **Fila** (`IGeocodingQueue` / `MySqlGeocodingQueue`): `TryGetCachedPoolAsync` (retorna o pool do dia UTC corrente, incrementa `hits`) e `StoreCachedPoolAsync` (upsert da resposta + poda das entradas com mais de 14 dias).
- **Ciclo do Worker**: antes de reservar unidade pela busca principal, o ciclo tenta o replay; só em *miss* reserva, consulta e armazena a resposta. O **segundo olhar** usa o mesmo mecanismo com a chave (logradouro-forma, cidade). A busca principal ganhou saidas explícitas de defer/terminal (fonte indisponível, robots), preservando todos os contadores, backs e o `ReleaseClaimAsync` da Sprint 65.
- Configuração: `Worker:Geocoding:QueryCacheEnabled` (padrão true) — entrada no `appsettings.json`.

## Decisões

- **Validade por dia UTC**: o mesmo relógio do teto diário. O OSM muda entre dias; dentro do dia, repetir a resposta real não inventa nada — é a fonte ela mesma, com minutos de distância.
- **Só sucesso entra no cache**: `http_*`, timeout, indisponibilidade e restrição de robots nunca são armazenadas. Nada negativo congela por um dia inteiro (o negativo de logradouro continua com a janela própria da Sprint 65).
- **Reuso entre fichas é o ponto central**: os ~2.200 endereços duplicados e todas as fichas de uma mesma rua (segundo olhar) passam a custar zero unidade após a primeira resposta real do dia.
- **Interação com a Sprint 65**: pool vazio rejogado ainda confirma a rua morta — o `RecordStreetEmptyAsync` renova a janela negativa, e a verdade subjacente tem menos de 24 horas.
- **Overpass fora desta Sprint**: o retry antigo agora custa apenas a unidade do próprio Overpass (o Nominatim deixou de entrar na conta). A sonda de frescor do espelho com estado persistente ("sticky") é o alavanca seguinte natural.
- **Reestruturação do bloco principal verificada por regressão total**: todos os testes de fila/painel/admin das Sprints 58–65 passaram sem alteração sobre o novo fluxo.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **498 testes unitários aprovados** (3 novos em `Sprint66QueryCacheTests`: round-trip JSON preservando campo a campo do candidato, serialização determinística, pool vazio) e **238 testes de integração aprovados** (1 novo: escopo por consulta exata — número/cidade diferentes não acertam cruzado —, expiração com a entrada envelhecida um dia UTC, contador de hits, re-gravação do dia criando apenas a linha de hoje), mais um preexistente ignorado. Teste de migração atualizado para 49 scripts.
- Ao vivo (evidência em `artifacts/sprint66-live-evidence.txt`): ficha 555 (PASSAGEM SANTA MATILDE/Belém, `overpass_stale_data`). Passada A pagou **+2 unidades** (Nominatim real + Overpass) e gravou o pool; passada C, reaplicada a mesma ficha com a consulta em cache, custou **+1 unidade** (só o Overpass — o Nominatim foi rejogado, `hits=1` confirmado na linha da chave). Entre as passadas, a primeira elegível da fila (rua morta) terminou `no_candidates_street` e deixou no cache dois pools vazios `"[]"` que cobrem as duplicatas daquela rua.
- Fundo operacional: entre 00:00 UTC e a parada para compilação, o Worker da geração anterior processou 225 unidades do novo dia — incluindo **13 rooftops aplicados** por confirmação em espelho antigo (caminho válido da Sprint 62) e 17 novas confirmações de rua esgotada.

## Operação

Worker parado durante compilação/testes e reiniciado via `scripts/windows/run-local-worker.ps1`; geração final viva: supervisor 22884 / worker 44668 (processo único verificado, heartbeat ok). Teto de 23/10/2026 UTC: 230/300 consumidas no momento da escrita; o restante da noite e os dias seguintes seguem com consultas redundantes zeradas. Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado. A ficha demo 555 ficou em `pending` com `attempts=2` (adiamento honesto aguardando espelho fresco do Overpass; o terceiro adiamento leva a `failed`, retomável pelo painel).

## Encerramento operacional

Cada consulta distinta que o dreno fizer passa a ser paga **no máximo uma vez por dia UTC**; endereços duplicados, retries de espelho antigo e o segundo olhar de ruas já respondidas deixam de consumir cota. Nenhuma coordenada nova foi aplicada nesta evidência; a frente do mapa segue com o Worker supervisionado.
