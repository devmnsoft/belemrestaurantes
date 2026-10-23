# Sprint 65 — cache negativo de logradouro: fim do gasto redundante em rua morta

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

O dreno da fila (~17 mil fichas, teto de 300 unidades/dia) mostrou o custo escondido: a maior parte das rejeições é "rua sem presença no OSM", e cada consulta confirmatória consome cota para chegar à mesma conclusão. A medição real da fila pendente quantificou a redundância — **6.406 logradouros distintos para 17.242 fichas; 12.868 fichas (75%) vivem em logradouro compartilhado** (ex.: RODOVIA AUGUSTO MONTENEGRO com 162 fichas na mesma cidade; AVENIDA BRASIL em 20 cidades). Uma vez comprovada, por busca real, que um logradouro+cidade não devolve candidato no Nominatim, as próximas fichas daquele mesmo escopo gastavam de novo 1–2 unidades para redescobrir a mesma ausência. Agora a confirmação é **persistida e reutilizada dentro de uma janela de validade**.

## O que mudou

- **Migração 048** (`048_street_negative_cache.sql`): tabela `geocoding_street_cache(street_key, city_key, learned_at)`, chave primária composta `(street_key, city_key)`.
- **Domínio** (`GeocodingRules`):
  - `StreetForm(address)`: logradouro sem o número final quando esse é o número da ficha (fator comum extraído da Sprint 64; comportamento de `StreetForSecondLook` preservado — refatoração coberta pelos testes existentes);
  - `StreetScopeForLookup(address, city)` + record `GeocodingStreetScope(StreetKey, CityKey)`: escopo estável **logradouro+cidade** normalizado (mesma normalização de `StreetKey`/`NameNormalizer`). Fichas com números diferentes caem no mesmo escopo; a mesma rua em outra cidade é escopo distinto ("AVENIDA BRASIL" em Belém não cobre "AVENIDA BRASIL" em Santarém). Nulo sem logradouro/cidade úteis (ex.: "SN" sem nome, cidade vazia).
- **Fila** (`IGeocodingQueue` / `MySqlGeocodingQueue`):
  - `IsStreetRecentlyEmptyAsync(streetKey, cityKey, validDays)`: true quando há registro gravado dentro da janela;
  - `RecordStreetEmptyAsync(streetKey, cityKey)`: upsert — só é chamado **após busca real de nível de logradouro ter zerado candidatos**.
- **Ciclo do Worker** (`GeocodingHostedService`):
  - ordem do loop: claim → **cheque de cache negativo → reserva de cota**. Acerto no cache encerra a ficha como terminal `no_candidates_street` consumindo **zero unidades**;
  - a confirmação que grava o cache ocorre quando a busca de nível de logradouro (segundo olhar da Sprint 64, ou a própria busca completa quando o endereço já é só logradouro) volta zero candidatos com fonte disponível e sem restrição de robots;
  - caso extremo de quota: com a reserva após o claim, uma ficha pega sem orçamento é devolvida por `ReleaseClaimAsync` — estado `pending`, lease limpa, **contador de tentativas restaurado ao valor anterior ao claim** e backoff pela fórmula padrão — e o ciclo para. Sem o rollback, o dreno de ruas mortas continuaria de graça, mas a frente da fila perderia tentativas à toa.
- **Configuração**: `Worker:Geocoding:StreetNegativeCacheEnabled` (padrão true) e `Worker:Geocoding:StreetNegativeCacheDays` (padrão 7, clamp 1–90). Entradas explícitas no `appsettings.json` do Worker (junto, formalizou-se a chave `StreetSecondLookEnabled` da Sprint 64).

## Decisões

- **Nunca inventar dados**: o cache herda apenas conclusão **negativa** verificada por busca real na mesma fonte, mesmo escopo, dentro da janela. Nenhuma coordenada nasce dele; a rejeição herdada carrega o mesmo token `no_candidates_street` — visível e re-enfileirável pelo painel (Sprints 63/59), e a re-enfileirada paga a reconfirmação (o upsert renova `learned_at` apenas por busca real, nunca por leitura).
- **Janela de 7 dias**: o mapa do OSM melhora com o tempo; expirada a janela, a primeira ficha do escopo consulta de novo e atualiza o registro. Mesma ordem de grandeza da regra de re-enfileiramento de terminais antigas (7 dias) — consistência operacional.
- **Escopo composto (logradouro, cidade)**: o negativo de uma cidade não se propaga a outra; a query real do Nominatim já é "(logradouro), (cidade), Pará, Brasil", então o escopo do cache espelha exatamente o que foi consultado.
- **Cache antes da reserva**: o benefício inteiro está em não consumir cota; checar depois de reservar desperdiçaria uma unidade por acerto. E mantém vantagem real em noites de teto esgotado: fichas de rua comprovadamente morta continuam drenando de graça.
- **Claim sem orçamento não conta como tentativa**: o incremento de `attempts` acontece no claim e `FinishAsync` nunca o reescreve; devolver a ficha pelo caminho normal de adiar faria o contador subir um ponto por ciclo enquanto o teto ficasse estourado, e três ciclos levariam a ficha a `failed` sem nenhuma consulta externa ter ocorrido. `ReleaseClaimAsync` restaura o contador (medido ao vivo na noite da Sprint: a primeira ressurreição do Worker ratchetou 1 ficha antes do rollback existir — restaurada).
- Reuso do token `no_candidates_street` (e não um motivo novo): para o operador continua sendo "rua esgotada de fato"; a distinção "verificada agora x herdada do cache" fica no histórico de execução (delta de cota), não na categoria exibida.
- Sem mudança de painel, rota ou flag de CLI: o recurso é interno ao ciclo; a visibilidade já vinha pronta da Sprint 63.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **495 testes unitários aprovados** (10 novos em `Sprint65StreetScopeTests`: equivalência de escopo entre números da mesma rua/cidade, separação por cidade, remoção do numeral final, casos nulos — e matriz de `StreetForm` preservando numerais de nome) e **237 testes de integração aprovados**, mais um preexistente ignorado. Novo `Sprint65StreetCacheTests` de integração cobre o armazenamento (escopo por cidade, janela curta cobrindo entrada fresca, expiração com registro envelhecido artificialmente e renovação por reconfirmação) e a regressão do rollback de claim (`ReleaseClaimAsync`: três ciclos de "pego sem orçamento" deixam `attempts` em 0 e o estado `pending`).
- Ao vivo (evidência em `artifacts/sprint65-live-evidence.txt`): três fichas reais da mesma rua/cidade comprovadamente ausente do OSM (RUA PEDRO PORPINO DA SILVA / Castanhal — a 579 já tinha confirmado a ausência na Sprint 64) foram re-enfileiradas e processadas num lote de 4 com cota alargada por variável de ambiente. O lote custou **+4 unidades exatas**: 2 pagaram a exploração da rua (completo + só logradouro, gravando a linha do cache) e as outras duas fichas da rua **encerraram em zero unidades** por acerto no cache; a quarta ficha (rua nova) gastou suas próprias 2 (Nominatim+Overpass, adiada `overpass_stale_data` como de costume). Sem o cache o lote custaria 6. A linha `geocoding_street_cache` surgiu com o escopo esperado (`rua pedro porpino da silva` / `castanhal`).
- Teste de migração: `CuiaExperienceV5Tests.Migrations_AreSequentialAndNonEmpty` atualizado para 48 scripts (convenção do projeto a cada nova migração).

## Operação

Worker parado durante compilação/testes e reiniciado via `scripts/windows/run-local-worker.ps1`; geração final viva: supervisor 32780 / worker 976. A Sprint envolveu várias paradas/partidas e houve um trecho em que a geração anterior (binário sem o rollback) ficou viva por acidente — varredura final de processos encerrou os restos e a única linha ratchetada nesse intervalo (ficha 814) foi restaurada (`attempts=0`) antes da partida limpa; varredura pós-partida: **zero** linhas `daily_limit` com tentativas pendentes. Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado. A partir deste dia, todo logradouro+cidade comprovadamente vazio economiza até 2 unidades por ficha seguinte enquanto durar a janela; com 75% da fila em ruas repetidas, a economia cresce sozinho conforme os escopos morrem (o efeito se mede no próprio `geocoding_runs`: `requested` vs. `granted` e no ritmo de terminais `no_candidates_street` sem aumento de cota). As fichas demo voltaram ao estado terminal honesto (rua de fato ausente); o caminho de operador segue sendo o painel.

## Encerramento operacional

A fila agora distingue "esta rua ainda pode ter algo" de "esta rua+cidade acabou de ser comprovada vazia" — e a segunda categoria não gasta cota de novo dentro da janela. Nenhuma coordenada publicada mudou nesta Sprint; 555/563/565/812 seguem `pending/overpass_stale_data` aguardando espelho fresco do Overpass.
