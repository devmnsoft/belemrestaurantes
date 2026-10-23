# Sprint 64 — segundo olhar para ruas sem candidato (`no_candidates`)

Execução local em 22/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

Após a Sprint 63, a segunda maior rejeição terminal era `no_candidates` (65 fichas, ~29% das terminais, e parcela esperada da fila de ~17 mil): o Nominatim não devolveu **nada** para o endereço completo. A amostra real mostrava que muitos são logradouros plausíveis em cidades onde a cobertura é esparsa — e que a presença do número/complemento na query costuma afundar a busca quando o OSM só conhece a rua. Antes de declarar ausência, agora vale uma segunda chance: **repetir a busca só com o logradouro**. A avaliação dos candidatos segue exatamente a mesma regra estrita — a busca muda, a confiança não.

## O que mudou

- **Domínio** (`GeocodingRules`):
  - `StreetForSecondLook(address)`: o logradouro para o segundo olhar — parte de `SplitStreetAddress` com o número final removido quando ele é o número da ficha ("AVENIDA V S 10, SN" → "AVENIDA V S"; "RUA 14 DE AGOSTO, 5" mantém o numeral interno do nome). Nulo quando repetir não mudaria a query (endereço que já é só logradouro) ou quando o nome resultante tem menos de 3 caracteres ("SN");
  - constantes estáveis `NoCandidates = "no_candidates"` e a nova `NoCandidatesStreet = "no_candidates_street"`.
- **Ciclo do Worker**: quando a busca completa devolve zero candidatos (e não está restrita/indisponível), o ciclo consulta mais uma vez só o logradouro. Cada consulta reserva **mais uma unidade do mesmo teto diário compartilhado no banco**; sem orçamento, adia com backoff (`daily_limit`) como qualquer outra chamada. Os candidatos do segundo olhar passam pelo mesmo `Evaluate` contra o endereço **original** (ficha com número continua exigindo número igual) e, se devolverem só logradouro, acionam a cadeia Overpass da Sprint 62 normalmente (âncora vira o logradouro encontrado).
- **Motivos**: rua encontrada → fluxo existente (aceite estrito / `unsupported_type` + Overpass); nada em nenhuma das duas buscas → terminal `no_candidates_street`, distinta da antiga `no_candidates` (que passa a significar "só a busca completa foi feita").
- Configuração: `Worker:Geocoding:StreetSecondLookEnabled` (padrão true). Sem nova migração, rota ou flag de CLI.

## Decisões

- **Relaxar a busca, nunca a aceitação**: o segundo olhar apenas troca a string enviada; estado/cidade/logradouro/número/ambiguidade continuam com as mesmas regras de sempre. Ficha sem número só pode virar `approximate`; ficha com número só vira rooftop com confirmação exata.
- Orçamento por chamada externa se estende à segunda consulta (1 unidade cada no contador diário do banco) — a cortesia continua mensurável, não heurística.
- `no_candidates_street` própria: o operador consegue ver (Sprint 63) e agir (re-enfileirar por motivo) sobre a categoria esgotada de fato, sem confundir com rejeições antigas que nunca tiveram o segundo olhar.
- Disparar só quando a primeira busca voltou **vazia**: `unsupported_type` já tem o caminho Overpass; outros motivos dominantes (ambiguidade, mismatch) não ganham nada com a mesma rua repetida.
- Regra de extração reutiliza `SplitStreetAddress` (mesma convenção de "número só como último token puro"), evitando uma segunda gramática de endereço no domínio.

## Validação

- Compilação Release sem avisos nem erros.
- Suítes completas: **485 testes unitários aprovados** (16 novos em `Sprint64StreetSecondLookTests`: matriz de extração do logradouro — número em parte separada/último token/numeral interno/rodovia/"SN"/vazio/espaçamento redundante — e estabilidade dos tokens de motivo; pool do segundo olhar ainda passando pela avaliação estrita) e **235 testes de integração aprovados**, mais um preexistente ignorado.
- Ao vivo (evidência em `artifacts/sprint64-live-evidence.txt`): ciclo real `--geocode-pending` (BatchSize=4, cota alargada por variável de ambiente, sem tocar arquivo versionado) processou as fichas 564 (Belém), 579 (Castanhal) e 615 (Juruti) — todas terminaram `skipped/no_candidates_street`, o que prova o segundo olhar executando e a ausência sendo real nestas ruas; a 4ª ficha processada (812, Barcarena, da fila de fundo) confirmou a composição com a Sprint 62: rua encontrada na busca completa, Overpass executado sobre espelho antigo e adiamento honesto `overpass_stale_data`. O contador diário subiu **+8 unidades = exatamente 2 por ficha** (Nominatim completo + [rua|Overpass]), confirmando o orçamento por chamada. Nenhuma coordenada foi aplicada nesta Sprint.

## Operação

Worker parado durante compilação/testes e reiniciado via `scripts/windows/run-local-worker.ps1` (supervisor 53388). Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado. Com o dreno da fila (~300 unidades/dia), o segundo olhar adiciona custo variável apenas onde há algo novo a tentar; fichas cujo endereço já é só logradouro continuam gastando uma unidade única. As fichas demo 564/579/615 voltaram via SQL (antecedente da Sprint 62); o caminho de operador segue sendo o painel.

## Encerramento operacional

Rejeições terminais agora distinguem o que esgotou de verdade (`no_candidates_street`) do que ficou pela primeira etapa (`no_candidates`); novas fichas passam pelas duas buscas antes de qualquer terminal. Fichas 555/563/565/812 seguem `pending/overpass_stale_data` aguardando espelho fresco. Nenhuma coordenada publicada mudou nesta Sprint.
