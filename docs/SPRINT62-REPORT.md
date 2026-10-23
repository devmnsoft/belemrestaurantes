# Sprint 62 — fallback Overpass para ampliar a cobertura de geocodificação

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

A Sprint 58 aplica coordenada quando o Nominatim devolve um objeto de nível de edifício/endereço que casa com a ficha. Quando o OSM **conhece o logradouro mas não tem objeto de endereço/edifício marcado nele** (a situação mais comum nas ruas mapeadas sem `addr:`), o Nominatim devolve só o logradouro e a ficha é rejeitada como `unsupported_type` — sem chance de segundo olhar. Agora, nesse caso exato, o Worker pergunta à Overpass API quais construções/restaurantes existem num raio pequeno ao redor do ponto do logradouro e aplica **somente** com confirmação estrita. Sem âncora única confiável, sem objeto confirmado ou com espelho de dados antigo, a ficha segue adiantando com backoff — nunca com palpite.

## O que mudou

- **Domínio** (`GeocodingRules`):
  - `FindStreetAnchor`: âncora = o primeiro candidato não aproveitável do Nominatim cujo logradouro casa com o da ficha e que está dentro da caixa do Pará (vários candidatos no mesmo logradouro não impedem — a ordem de relevância do Nominatim decide);
  - `EvaluateBuildings` + registro `OsmBuildingElement`: avalia o conjunto de construções devolvido pela Overpass —
    - ficha **com número**: aceita apenas objeto com `addr:housenumber` igual e logradouro compatível (conteúdo bidirecional da chave de rua); exatamente um objeto distinto (duplicatas com geometria ~1 m contam uma vez) → **rooftop**; zero → `overpass_no_match`; dois ou mais → `overpass_ambiguous`;
    - ficha **sem número**: um único objeto confirmado pelo logradouro → **approximate**; ambiguidade ou ausência → motivos estáveis;
    - elementos fora da caixa do Pará são descartados antes de qualquer contagem.
- **Infraestrutura** (`OverpassBuildingSource` + `OverpassOptions`): cliente da Overpass API pelo mesmo `ISafeHttpClient` (UA identificado), com cadência mínima de 2,1 s entre requisições e **failover entre instâncias públicas** em ordem de preferência (`overpass-api.de`, `kumi.systems`, `osm.ch`), partindo sempre da última que respondeu bem. A resposta traz `osm3s.timestamp_osm_base`, que a fonte lê e classifica: snapshot com mais de 14 dias (ou idade ilegível) marca `DataStale`.
- **Consulta Overpass** (raio configurável, padrão 80 m): `building=*`, amenidades de comida, `shop=restaurant` e **qualquer objeto com `addr:housenumber`** — o último termo pega malls/ways que abrigam o número do endereço mesmo sem tag `building`. A consulta é gerada **multilinha**: as instâncias públicas rejeitam a mesma união em linha única com parse error no OSM3S (reproduzido e travado por teste).
- **Ciclo do Worker**: quando a rejeição do Nominatim é `unsupported_type` e há âncora, a consulta Overpass consome **mais uma unidade do mesmo teto diário compartilhado no banco** (orçamento por chamada externa, conservador; sem cota, adia com backoff). Resultado da avaliação:
  - aceito → aplica com origem `OpenStreetMap via Overpass (objeto {id})`, histórico `overpass_osm` com referência distinta e confiança da tier (0.800 rooftop / 0.550 approximate);
  - rejeitado sobre espelho **fresco** → motivo estável terminal (`overpass_no_match` / `overpass_ambiguous`);
  - rejeitado sobre espelho **antigo** → **adiamento** `overpass_stale_data` (snapshot velho só pode confirmar, não provar ausência);
  - falha de rede em todas as instâncias → adiamento com motivo agregado por endpoint.
- **Guard expandido (Sprints 60/61)**: o prefixo gerenciável por revisão humana passa de `OpenStreetMap via Nominatim%` para `OpenStreetMap via%` no revert, no ajuste e nos cartões de qualidade — uma coordenada aplicada via Overpass entra no mesmo ciclo humano aplicar → ajustar → reverter → re-enfileirar.
- Configuração: `Worker:Geocoding:OverpassEnabled` (padrão true) e `Worker:Geocoding:OverpassRadiusMeters` (padrão 80); `Overpass:Endpoints` na ordem de preferência. Sem nova migração; sem nova rota ou flag de CLI.

## Decisões

- Âncora pela relevância, não unicidade: o caso real de DELALE mostrou o Nominatim devolvendo mall + bicicleta com o mesmo logradouro/número; exigir "um único candidato" matava exatamente os casos com mais informação.
- Confirmação exige número igual **e** logradouro compatível quando ambos existem; sem tag, a rua não contradiz (mesma convenção da regra principal), mas sem número na ficha nada vira rooftop.
- Orçamento por chamada externa: cada consulta Overpass reserva mais uma unidade no contador diário do banco — o teto continua sendo uma política de cortesia mensurável, não uma heurística em memória.
- Ordem dos espelhos prioriza dados frescos; o espelho antigo-rápido fica último e só confirma. A última instância bem-sucedida é memorizada por processo (não entre instâncias do Worker, que nunca sobrepõem ciclos).
- Espelho antigo rejeita nada: `timestamp_osm_base` além de 14 dias (ou ilegível — observado na `osm.ch` com valor "117921") transforma a rejeição em adiamento; a adoção com objeto confirmado continua valendo porque edifício endereçado não muda de lugar.
- Mesma separação das sprints anteriores: SQL e rede só em Infraestrutura, regras de aceitação puras no Domínio, Web não toca neste caminho.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **469 testes unitários aprovados** (27 novos em `Sprint62OverpassTests`) e **233 testes de integração aprovados**, mais um preexistente ignorado. Os novos cobrem: parsing de node/way/relation com tags `addr:*`; âncora por relevância, logradouro divergente e fora da caixa; avaliação com número (confirmado, rua errada, ambíguo, duplicata de geometria) e sem número (único, múltiplos, rua conflitante, fora da caixa, lista vazia); failover de endpoint com stub HTTP e motivo agregado em falha total; idade dos dados (ISO recente/velho, formato ilegível, epoch, fronteira de 14 dias, payload quebrado).
- Ao vivo (evidência em `artifacts/sprint62-live-evidence.txt`, reproduções em `artifacts/sprint62-*.ps1`): diagnóstico das três instâncias públicas nesta máquina — `overpass-api.de` em HTTP 406 geral (bloqueio aparente por faixa de IP), `kumi.systems` sob forte carga com snapshot de 28/07 e `osm.ch` rápido mas antigo (sem o way que o Nominatim indexa no endereço-teste). Dois ciclos reais do Worker com as fichas 555/563/565 (todas em `unsupported_type`) e 4ª ficha de fila: primeiro ciclo provou o caminho ponta a ponta (`overpass_no_match` estáveis + `no_candidates` preservada na ficha sem âncora); segundo ciclo, já com a regra de frescor, deixou as três fichas `pending/overpass_stale_data` com backoff de 60 min — **adiar em vez de inventar**, exatamente quando nenhum espelho fresco estava acessível. Nenhuma coordenada real foi aplicada nesta Sprint.

## Operação

O Worker local ficou parado durante compilação e testes e volta a operar via `scripts/windows/run-local-worker.ps1`. Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails; a cota diária regular (300) era a dos ciclos de hoje e a sobra usada na demonstração saiu por variável de ambiente, sem tocar arquivo versionado. Enquanto `overpass-api.de` persistir em 406 para esta máquina, as fichas sem objeto de endereço seguem sendo atendidas na ordem da fila pelos espelhos alternativos, com adiamento honesto quando só resta dado antigo.

## Encerramento operacional

Worker reiniciado após a última compilação, com estado e heartbeat confirmados em `artifacts/worker-local/state.json`. As três fichas de demonstração (555, 563, 565) seguem `pending` aguardando novo ciclo — nenhuma coordenada publicada mudou nesta Sprint.
