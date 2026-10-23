# Sprint 69 — o frescor dos espelhos Overpass sai da memória do Worker e chega ao painel: cada espelho responde por si no `/admin/geocodificacao`

Execução local em 23/10/2026. Sem push ou deploy (commit no repositório local nesta Sprint). Credenciais permanecem fora do código.

## Origem

A Sprint 67 instalou o frescor sticky dos espelhos: o Worker observa a idade dos dados de cada espelho Overpass (`osm3s.timestamp_osm_base` de toda resposta real) e, com todos confirmados velhos dentro da janela (60 min), adia fichas em `overpass_stale_data` gastando zero cota e zero HTTP. O problema operacional que ficou: **o estado vive só na memória do processo do Worker**. Com ~87 fichas paradas em `overpass_stale_data` e os espelhos públicos de/kumi em timeout e o osm.ch respondendo com carimbo ilegível, o operador não tinha como responder "por que essas fichas seguem adiando?" sem ler log — e o painel `/admin/geocodificacao`, que é a superfície viva dessa operação, não mostrava nada sobre os espelhos.

## O que mudou

- **Migração 051** (`051_overpass_mirror_freshness.sql`): tabela `overpass_mirror_freshness(endpoint PK, base_timestamp DATETIME(6) NULL, observed_at DATETIME(6), created_at DATETIME(6))`, `utf8mb4_unicode_ci` como as demais. Uma linha por espelho; a última resposta vence (upsert); `base_timestamp NULL` codifica **idade ilegível** — o formato que o próprio espelho devolveu sem ser ISO-8601 nem epoch plausível.
- **Application** (`IGeocodingServices.cs`): record `MirrorFreshnessSnapshot(Endpoint, BaseTimestampUtc, ObservedAtUtc)` com o veredito puro `IsDataOld(now, maxDataAgeDays = 14)` — a mesma regra conservadora da fonte (ausente, ilegível ou mais de 14 dias = antigo; exatamente 14 dias ainda vale) — e a interface `IOverpassMirrorFreshnessStore` (registro upsert + leitura das últimas observações).
- **Infrastructure**: `MySqlOverpassMirrorFreshnessStore` (upsert `ON DUPLICATE KEY UPDATE`; grava wall-clock UTC na convenção `DATETIME(6)` do repositório) e a `OverpassBuildingSource` ganha um parâmetro **opcional** `IOverpassMirrorFreshnessStore?` — no caminho de sucesso, após registrar a observação em memória (S67 intacta), persiste **o mesmo fato**; falha de persistência só loga e a busca segue com a resposta. Testes de unidade existentes constroem a fonte sem store e continuam passando intactos.
- **Web**: `AdminGeocodingController` injeta a store e o `OverpassOptions` (singleton já registrado; a Web usa o padrão dos três espelhos públicos) e o painel ganha a seção **"Espelhos Overpass (frescor dos dados)"**: para cada espelho configurado, a data-base que ele respondeu, a idade, quando foi observado (UTC + relativo) e o veredito conservador em badge — fresco / dado antigo / dado antigo (idade ilegível) / sem observação. Rodapé da página estendido até os relatórios da Sprint 69.
- **Testes**: `Sprint69MirrorFreshnessUnitTests` (5 casos puros: fresco, fronteira exata de 14 dias, 14 dias + 1 s, idade ilegível, teto customizado) e `Sprint69MirrorFreshnessTests` de integração (round-trip/upsert/NULL do store contra o banco real, e exposição autenticada do painel com login MFA temporário no template Sprint 63, assertando os dois vereditos e a data-base exibida). Bump da contagem embutida de migrações 50→51 em `CuiaExperienceV5Tests`.

## Decisões

- **Persiste fato, não julgamento**: a linha é exatamente o que o espelho respondeu (carimbo-base e instante da observação). O veredito antigo/fresco é recalculado por quem lê, com a regra de 14 dias — a persistência nunca congela opinião. "Nunca inventar dados": `NULL` entra onde o espelho foi ilegível; ausência de linha significa "sem observação", nunca suposição.
- **A memória manda, o banco mostra**: o short-circuit zero-cota da S67 continua usando apenas o estado in-memory (janela de 60 min, reinício re-sonda no máximo uma vez por espelho). A tabela existe para observabilidade humana, não para controlar o ciclo — um processo novo não pula espelho porque "ontem estava velho".
- **Escrita best-effort na busca**: um upsert local de ~1 ms no caminho crítico é aceitável; uma falha dele não pode custar a resposta que o espelho deu — por isso try/catch com log e continuação (a busca em memória segue válida).
- **Sem nova superfície de configuração**: nem a Web nem o Worker ganharam chaves. A Web lista os espelhos **da sua própria configuração** (padrão = os mesmos três públicos do Worker hoje); se um dia um ambiente trocar a lista, o painel mostrará a lista daquele processo — honesto, mas exigindo manter os dois arquivos em sincronia (registrado aqui, sem duplicar valor em dois lugares).
- **Convenção de fuso idêntica ao repositório**: `DATETIME(6)` gravado como wall-clock UTC e lido com `.ToUniversalTime()` (o driver devolve `Kind.Unspecified`; o deslocamento de sessão é uniforme, se cancela em diferenças e mantém o painel consistente com as demais telas que usam o mesmo padrão). Verificado empiricamente com probe dedicado antes de assinar a convenção.

## Validação

- Compilação Release sem avisos nem erros (`--no-incremental`).
- Suítes completas: **521 testes unitários aprovados** (5 novos em `Sprint69MirrorFreshnessUnitTests`) e **239 de integração aprovados** mais 1 preexistente ignorado, de 241.
  - Transparência sobre a suíte de integração: `CuiaAiEndpointTests.Ask_WithValidToken_ReturnsJson` **falhou nesta rodada e falha idêntico no commit pai** (`9853454`, verificado em worktree isolada contra o mesmo banco): o teste depende da resposta do provedor LLM gratuito externo (Gemini/Groq) no momento da execução — nesta janela ele devolveu saudação em vez da frase canônica de busca vazia. Não é diff desta Sprint; documentado para não parecer regressão silenciosa.
  - Unidade: houve **uma** falha isolada de um teste `CuiaAiTests` numa rodada completa (passou em execução isolada e na repetição completa — 521/521), também fora do escopo desta Sprint e registrada pelo mesmo princípio.
- Ao vivo (evidência em `artifacts/sprint69-live-evidence.txt`): one-shot `--geocode-pending` com orçamento limitado só ao processo (`Worker__Geocoding__DailyLimit=400`, `BatchSize=3`) processou 3 fichas reais — 1 adiada em `overpass_stale_data` (osm.ch respondeu com dado antigo), 2 rejeitadas (`no_candidates_street`, `unsupported_type`) — e o Worker **persistiu a primeira observação real**: `overpass.osm.ch`, `base_timestamp=NULL` (carimbo ilegível, como documentado), `observed_at=2026-10-23 14:06:14 UTC`. A rota `/admin/geocodificacao` respondeu 302 para login sem credencial; a renderização autenticada da seção está coberta pelo teste de integração com usuário MFA temporário.

## Operação

Worker parado durante compilação/testes; geração final viva: supervisor 53764 / worker 50888, processo único verificado, heartbeat `running`, **"Migrações verificadas: 0 aplicadas, 51 já em vigor"**, diagnóstico de configuração da S68 sem warnings, padrões do `appsettings.json` (ciclo de geocodificação 10 min). Os env overrides de evidência valeram só para o one-shot. Cota do dia: `granted=305/300` — esgotada para o ciclo supervisionado, o que a própria evidência demonstra: o adiamento zero-cota da S67 continua funcionando, e agora com cara no painel. Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado; nenhum segredo adicionado ou versionado.

## Encerramento operacional

A pergunta que antes exigia log ("por que as fichas seguem em `overpass_stale_data`?") tem resposta na tela: o painel mostra, espelho por espelho, a data-base que cada um respondeu, quando foi vista e o veredito conservador — e quando o espelho foi ilegível, a linha diz *idade ilegível* em vez de fingir precisão. O que o Worker sabia na memória agora também está no banco, escrito como fato, e o operário humano voltou a conseguir operar a fila sem adivinhar.
