# Worker local contínuo

O supervisor de desenvolvimento executa o Worker Release numa janela oculta, com lease exclusivo, estado e logs em `artifacts/worker-local`. Não instala serviço, tarefa agendada ou inicialização do Windows. Fechar o Windows/desligar a máquina interrompe essa operação.

## Configurar sem versionar segredos

Use o user-secrets do projeto Worker, separadamente do Web. Configure banco, chave do provedor confirmado gratuito e `MailEncryption:KeyRingPath` no cofre; nunca coloque chaves nos comandos de documentação, arquivos de configuração versionados ou relatórios. Web e Worker devem compartilhar persistentemente o mesmo diretório privado de proteção de e-mail/documentos. Preserve também `document-lookup.v1.bin` e suas chaves Data Protection em backup compatível.

Configuração operacional local utilizada na Sprint 48:

- `Database:ApplyMigrations=false`: migrações já aplicadas e verificadas antes da execução contínua.
- `Discovery:Enabled=true`, `Discovery:Provider=OpenStreetMap`, `Discovery:IntervalMinutes=10`, `Discovery:BatchSize=1`.
- `Crawler:Enabled=true`, `Crawler:IntervalMinutes=10`, `Crawler:MaxTasksPerCycle=2`.
- Gemini confirmado gratuito; orçamento monetário zero; reservas globais e por finalidade documentadas em [FREE-AI-SETUP.md](FREE-AI-SETUP.md).
- SMTP desabilitado; o Worker não comprova entrega de mensagens nessa configuração.

O perfil Development é definido apenas no processo local. Não replique esse perfil em produção. Pausas e orçamentos persistentes continuam valendo. Um conector pode estar saudável mesmo sem trabalho elegível; confira ciclos, fontes e datas, além do heartbeat.

## Iniciar e parar

Na raiz do repositório, com a execução anterior parada e migrações aplicadas:

```powershell
dotnet build BelemRestaurantes.sln -c Release -m:1 -nr:false
./scripts/windows/run-local-worker.ps1
Get-Content artifacts/worker-local/state.json
```

O supervisor registra o PID do próprio Worker e reinicia somente seu processo após saída, com atraso de 5 a 60 segundos. Uma segunda inicialização é recusada enquanto o lease estiver ocupado.

```powershell
./scripts/windows/stop-local-worker.ps1
Get-Content artifacts/worker-local/state.json
```

A parada cria um arquivo de solicitação; o Worker encerra ordenadamente e o supervisor deixa de reiniciar. Aguarde `state='stopped'` antes de recompilar ou trocar DLLs. Se o prazo de parada expirar, inspecione os logs e jobs antes de qualquer encerramento manual; não mate outros processos .NET.

Para retomar, execute novamente `run-local-worker.ps1`. Ele remove a solicitação anterior. Filas, leases e cursores permanecem no banco; jobs com lease expirado podem ser recuperados pelo fluxo normal. A retomada não zera cotas nem publica candidatos automaticamente.

## Conferir progresso real

```powershell
$env:DOTNET_ENVIRONMENT='Development'
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet src/BelemRestaurantes.Worker/bin/Release/net10.0/BelemRestaurantes.Worker.dll --verify-sprint48
```

Essa inspeção é somente leitura: heartbeat, catálogo, qualidade agregada, alvos, filas, pausas, limites monetários e tentativas gratuitas. Não imprime chaves, conversas ou documentos. Confira se o PID existe e se o heartbeat é recente, não apenas o texto `running`.

Painéis: `/admin/operacao`, `/admin/discovery`, `/admin/crawler` e `/admin/referencias`, usando uma conta administrativa autorizada com MFA. O horário de elegibilidade de um alvo não garante execução imediata: há fila, prioridade, cadência e disponibilidade da fonte.

CLI pontual de manutenção disponível, sem executar automaticamente ao iniciar:

- `--schedule-belem-open-data`: ajusta somente alvos de Belém já habilitados com fonte padrão/OSM; preserva prioridades e pausas. Use após conferir a configuração.
- `--import-open-data --city=Belém --max-records=80`: importação limitada usando cache. `--refresh-open-data` solicita atualização externa sujeita a prazo e política da fonte.
- `--import-place-reference <arquivo>` e `--approve-place-reference <id>`: operação editorial local auditada. Preferir revisão autenticada pela interface no uso normal. Importar não aprova nem publica.
- `--probe-free-ai <nome>`: faz chamada real limitada a partir de referência revisada e consome reserva. Não usar como health check periódico.
- `--geocode-pending`: aplica migrações, executa um ciclo de geocodificação (Nominatim real, com fallback Overpass quando há só logradouro) e imprime `{Enqueued, Processed, Placed, Skipped, Deferred}`.
- `--verify-geocoding`: somente leitura; imprime a contagem da fila de geocodificação, a reserva diária usada e a distribuição das rejeições terminais por motivo (`SkipReasons`).
- `--revert-geocode <id>`: remove a coordenada aplicada pelo OSM/Nominatim (ou já ajustada por revisão administrativa) em uma ficha sem proteção Tier 1, devolve a linha da fila a `skipped/reverted_by_admin` e grava histórico `admin_review` + auditoria `geocoding.revert`. Ator `worker-cli`. Não re-enfileira sozinha.

Scripts de instalação como serviço existentes no projeto exigem configuração apropriada do ambiente e implantação autorizada. Não foram executados nesta Sprint.


## Sprint 50: revisão editorial

`EditorialReviewHostedService` verifica a cada hora roteiros publicados com revisão superior a 90 dias. Registra uma pendência durável por versão, para revisão humana em `/admin/roteiros`; não confirma sozinho fontes, horários ou funcionamento. Requer migração 039. O salvamento administrativo mantém histórico e fecha a pendência da versão anterior. O Worker foi retomado após os testes; confira processo e heartbeat conforme os comandos acima.


## Sprint 51: pesquisa recorrente e publicação por critérios

A migração 040 acrescenta `research_jobs`. `Worker:ResearchEnabled` controla pesquisas recorrentes, e `Worker:ResearchBatchSize` limita o lote (local: 2 a cada 30 minutos). Sem provedor geral confirmado/configurado, o serviço registra a dependência e não usa a Wikipédia para fabricar fichas comerciais. Fontes e alvos pausados continuam respeitados. O painel `/admin/crawler` apresenta resultados dos jobs.

`Crawler:AutoPublication:Enabled` e `MaxPerCycle` habilitam publicação de novas fichas com identidade estruturada, município e endereço explícitos, fonte correspondente e sem conflitos/duplicação. Habilitado localmente com máximo 3 por ciclo/instância; não promove para Guia nem libera mídias. Auditoria e suspensão usam o fluxo existente. Outros ambientes são desabilitados por padrão. Veja limites e homologação em [SPRINT51-REPORT.md](SPRINT51-REPORT.md).


## Sprint 52: biblioteca de referências e feeds

A migração 041 acrescenta `external_content` e o modo `content_only` de fontes. Use a opção “somente referências” no cadastro do crawler para coletar RSS/Atom e páginas de matérias sem criar restaurantes. Fontes desse modo são conferidas a cada hora, respeitando a cadência da origem e o intervalo do Worker. O feed atual da Agência Brasil foi configurado nesse modo (ID 31), com filtro de assunto e pertinência regional.

CLI: `--reference-feed=<URL HTTPS> --city=Belém` valida antes de cadastrar. `--import-reference-metadata <JSON>` aceita lote limitado de metadados; não publica o corpo recebido. A biblioteca aparece em `/referencias`, Home, Descobrir e no contexto da Cuia. Suspensão em `/admin/conteudos-externos` é auditada e persiste após recoleta. Prévia PNG é uma imagem do cartão do portal, não screenshot da matéria original. Veja [SPRINT52-REPORT.md](SPRINT52-REPORT.md).


## Sprint 58: geocodificação autônoma

A migração 047 acrescenta `geocoding_tasks` e `geocoding_runs`. `Worker:Geocoding:Enabled` (padrão true) habilita o serviço, que roda um ciclo a cada `Worker:Geocoding:IntervalMinutes` (local: 10); `Worker:Geocoding:BatchSize` limita o lote (10) e `Worker:Geocoding:DailyLimit` limita requisições ao Nominatim por dia (300), reserva compartilhada entre instâncias no banco. `Nominatim:BaseUrl` aponta a instância (padrão público).

O ciclo só toca fichas sem coordenada, com logradouro e cidade mínimos, fora da proteção Tier 1 (dono ou reivindicação aprovada). Coordenada entra uma vez, com precisão (`rooftop`/`approximate`) e origem "OpenStreetMap via Nominatim (objeto {id})". Rejeição termina como `skipped` com motivo estável; falha de fonte adia com backoff e desiste após três tentativas. O painel `/admin/geocodificacao` (moderador + MFA) acompanha fila, motivos e devolve rejeições à fila; `--verify-geocoding`, logs e banco continuam válidos. Uma coordenada aplicada pelo OSM pode ser revertida por revisão humana (painel ou `--revert-geocode <id>`), sem tocar Tier 1 nem origem alheia. Veja [SPRINT58-REPORT.md](SPRINT58-REPORT.md), [SPRINT59-REPORT.md](SPRINT59-REPORT.md) e [SPRINT60-REPORT.md](SPRINT60-REPORT.md).


## Sprint 62: fallback Overpass para ampliar cobertura

Quando o Nominatim devolve apenas o logradouro (`unsupported_type`) e há uma âncora dentro da caixa do Pará, o ciclo consulta a Overpass API (`IOsmBuildingSource`/`OverpassBuildingSource`) num raio de `Worker:Geocoding:OverpassRadiusMeters` (padrão 80 m) em torno dela e aplica somente com confirmação estrita:

- **ficha com número** → aceita apenas objeto com `addr:housenumber` igual e logradouro compatível, um único objeto distinto → `rooftop`; senão `overpass_no_match` / `overpass_ambiguous`;
- **ficha sem número** → um único objeto confirmado pelo logradouro → `approximate`.

A consulta é gerada multilinha (instâncias públicas rejeitam a união em linha única), com cadência mínima de 2,1 s e **failover entre instâncias públicas** em `Overpass:Endpoints` (padrão `overpass-api.de`, `kumi.systems`, `osm.ch`), partindo da última que respondeu bem. Cada consulta consome **mais uma unidade do mesmo teto diário** (`Worker:Geocoding:DailyLimit`) no banco. A fonte lê `osm3s.timestamp_osm_base`: snapshot com mais de 14 dias (ou idade ilegível) marca `DataStale`, e sobre espelho antigo a rejeição vira adiamento `overpass_stale_data` (só confirma, não prova ausência); a falha total agrega o motivo por endpoint. `Worker:Geocoding:OverpassEnabled` (padrão true) liga/desliga o fallback. Origem aplicada vira "OpenStreetMap via Overpass (objeto {id})" e entra no mesmo ciclo humano de reverter/ajustar dos prefixos `OpenStreetMap via%`. Sem nova migração nem rota. Veja [SPRINT62-REPORT.md](SPRINT62-REPORT.md).

## Sprint 63: visibilidade e retomada por motivo de rejeição

O resumo da fila (`SummaryAsync`, usado pelo painel e por `--verify-geocoding`) passa a distribuir as linhas terminais rejeitadas pelo último motivo (`SkipReasons`, em ordem decrescente). O painel ganha uma seção "Motivos de rejeição nas linhas terminais" com um botão **Re-enfileirar** por motivo: `POST /admin/geocodificacao/reenfileirar-por-motivo` devolve à fila (`pending`, `attempts=0`) todas as linhas `skipped`/`failed` com aquele motivo cuja ficha ainda não tem coordenada, com auditoria `geocoding.requeue_by_reason` (motivo e quantidade). É ação humana, como as demais: não consome cota na hora — cada ficha reservada volta a ocupar unidades do teto diário quando o ciclo efetivamente consultar. Caso típico: capacidade nova torna o motivo recuperável (o lote de `unsupported_type` agora passa pelo fallback Overpass da Sprint 62). Veja [SPRINT63-REPORT.md](SPRINT63-REPORT.md).

## Sprint 64: segundo olhar para ruas sem candidato

Quando a busca completa do Nominatim não devolve **nenhum** candidato, o ciclo repete a busca uma única vez com **só o logradouro** (`GeocodingRules.StreetForSecondLook`: parte do endereço sem o número final quando ele é o número da ficha; nomes curtos tipo "SN" e endereços que já são só rua não disparam). Cada consulta reserva mais uma unidade do mesmo teto diário; sem orçamento, adia com backoff. Os candidatos da segunda busca passam pela mesma `Evaluate` contra o endereço original (número continua exigindo confirmação exata); rua encontrada segue normalmente para a cadeia Overpass da Sprint 62. Terminal quando as duas buscas ficam vazias: `no_candidates_street` (a antiga `no_candidates` passa a significar que apenas a busca completa foi feita). `Worker:Geocoding:StreetSecondLookEnabled` (padrão true) liga/desliga. Veja [SPRINT64-REPORT.md](SPRINT64-REPORT.md).

## Sprint 66: cache de consulta do Nominatim

Antes de reservar unidade do teto diário, o ciclo consulta `geocoding_query_cache` (migração 049) por uma chave SHA-256 da **consulta exata** que o `/search` recebe (mesmo `BuildQuery` da fonte): se a mesma consulta (endereço, cidade) foi respondida de verdade no **dia UTC corrente**, o pool de candidatos é rejogado gastando zero unidade e alimentando a avaliação estrita de sempre; caso contrário, o ciclo reserva, consulta e armazena a resposta. O **segundo olhar** (Sprint 64) usa o mesmo mecanismo com (logradouro-forma, cidade), então todas as fichas de uma rua reaproveitam a resposta daquela rua. Só buscas com fonte disponível e sem restrição de robots entram no cache — falha/restrição nunca congela; entradas expiram com o dia UTC em que foram aprendidas e são podadas após 14 dias. O contador `hits` da tabela mede a economia. `Worker:Geocoding:QueryCacheEnabled` (padrão true) liga/desliga. Veja [SPRINT66-REPORT.md](SPRINT66-REPORT.md) e a evidência ao vivo em `artifacts/sprint66-live-evidence.txt`.

## Sprint 65: cache negativo de logradouro

Quando uma busca real de **nível de logradouro** (o segundo olhar acima, ou a própria busca completa quando o endereço já é só rua) devolve zero candidatos com a fonte disponível e sem restrição, o ciclo grava a confirmação em `geocoding_street_cache` por escopo **(logradouro normalizado, cidade normalizada)** — `GeocodingRules.StreetScopeForLookup`, a mesma normalização de comparação de ruas, com a cidade separando escopos ("AVENIDA BRASIL" em Belém não cobre Santarém). Nos ciclos seguintes, uma ficha cujo escopo consta no cache dentro da janela (`Worker:Geocoding:StreetNegativeCacheDays`, padrão 7, clamp 1–90) é encerrada como `no_candidates_street` **antes** de reservar qualquer unidade do teto diário — zero custo. Quando o teto está esgotado, a ficha pega sem orçamento volta por `ReleaseClaimAsync` (`pending`, lease limpa, **tentativas restauradas**) e o ciclo para: o dreno de ruas mortas segue de graça e nada chega a `failed` por falta de cota. A renovação só ocorre por busca real (upsert de `learned_at`); re-enfileirar a ficha pelo painel e deixá-la ser reclamada fora da janela força a reconfirmação. `Worker:Geocoding:StreetNegativeCacheEnabled` (padrão true) liga/desliga. Motivação medida: 75% das ~17 mil fichas pendentes vivem em logradouro compartilhado, então cada rua comprovadamente vazia economiza até 2 unidades por ficha seguinte. Migração 048. Veja [SPRINT65-REPORT.md](SPRINT65-REPORT.md) e a evidência ao vivo em `artifacts/sprint65-live-evidence.txt`.
