# Sprint 71 — o frescor dos espelhos chega ao CLI `--verify-geocoding`, com fonte única do veredito (e dois bugs reais que o teste arrancou da madeira)

Execução local em 23/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

Depois das Sprints 67/69, o frescor dos espelhos Overpass era visível num único lugar: o painel `/admin/geocodificacao` (navegador + sessão de moderador + MFA). A superfície que um operador usa de SSH na VPS — o `--verify-geocoding` do Worker, que desde a Sprint 63 distribui também os motivos de rejeição — mostrava a fila, mas **não os espelhos**. Quem precisa responder "por que as fichas seguem adiando em `overpass_stale_data`?" tinha que abrir banco ou navegador. Esta Sprint completa a história nas duas superfícies com uma **fonte única** do veredito conservador, e o caminho de validação acabou descobrindo dois defeitos reais que nenhum teste anterior alcançava.

## O que mudou

- **`MirrorFreshnessViewRow` (+ `Merge`)** em `Application.Abstractions` (ao lado do snapshot da Sprint 69): mescla os espelhos **configurados** com as observações **persistidas** e decide o veredito conservador num único lugar (`sem` / `ilegivel` / `antigo` / `fresco`), normalizando endpoints (barra final, duplicados, vazios) e extraindo host sem quebrar em string não-URL. Regras explícitas e testadas: espelho configurado sem linha persistida aparece como "sem observação" (ausência nunca vira suposição); linha persistida de espelho que saiu da configuração não é exibida (permanece na tabela como história).
- **Painel `/admin/geocodificacao`** passa a usar o helper — mesmos rótulos e vereditos de antes, menos uma cópia da lógica.
- **`--verify-geocoding`** agora imprime `{ "queue": <resumo anterior intacto>, "mirrors": [...] }` — cada espelho configurado com endpoint, host, datas-base (UTC) e status. O formato segue o precedente do próprio Worker (`--culinary-report` já aninhava `{report,queue}`); verificado que nenhum teste automatizado consome o formato antigo e o doc humano foi atualizado. A fila continua sendo a mesma contagem de sempre, só agora dentro da chave `queue`.

## O que a validação revelou (duas falhas reais, não cosméticas)

1. **NRE latente no painel para idade ilegível.** O código antigo formava a data-base com `snap.BaseTimestampUtc!.Value.ToString(...)`: para a linha cuja base é `NULL` — exatamente o caso "idade ilegível", e **exatamente a linha osm.ch de produção de hoje** (o espelho responde com `timestamp_osm_base='117940'`, que não se lê como data) — a página derrubaria `NullReferenceException`. Os testes da Sprint 69 só tinham semeado bases legíveis. O helper calcula o veredito antes de qualquer formatação e o novo caso de integração (base NULL semeada, página renderiza com "dado antigo (idade ilegível)") protege a regressão.
2. **O `switch` dos badges era texto morto no atributo `class`.** Ao dumpar o HTML real da página, o atributo veio como `class="badge ilegivel switch { "fresco" => "bg-success", ... }"`: dentro de um valor de atributo, o Razor não interpreta expressão `switch` sem parênteses, então as classes de cor (`bg-success`/`bg-warning`/`bg-secondary`) **nunca foram aplicadas** — nem nos espelhos (Sprint 69), nem na lista de fichas. Correção de uma linha por ocorrência: `@(m.Status switch { ... })`. A pista que levou ao dump foi ela mesma um ensinamento do repositório: o encoder HTML do Razor escreve acentos como entidade numérica (`í` → `&#xED;`), o mesmo fenômeno já documentado para `>`/`≤` — a asserção agora compara a forma codificada.

## Validação

- Build Release `--no-incremental`: **0 avisos / 0 erros**.
- Unidade: **528/528** (521 da base + 7 novos do helper: normalização/desduplicação, sem observação, fresco, antigo, fronteira exata de 14 dias ainda fresca, idade ilegível sem lançar, configuração nula/vazia).
- Integração local: **240 aprovados / 1 ignorado (verificação opcional de credenciais, preexistente) / 0 falhas** — nesta execução até o `CuiaAiEndpointTests.Ask_WithValidToken_ReturnsJson` (o dependente de LLM gratuito classificado como ambiental na Sprint 69) passou.
- Evidência viva do `--verify-geocoding` com o binário novo e a sonda real aos três espelhos: `artifacts/sprint71-live-evidence.txt`.

## Estado operacional colhido na janela (fatos reais, 23/10 ~15:07–15:25 UTC)

- Sonda direta aos três espelhos: `overpass-api.de` → HTTP 406 (~1 s); `overpass.kumi.systems` → timeout (25 s); `overpass.osm.ch` → HTTP 200 em 1,3 s, mas com `timestamp_osm_base='117940'` ilegível → conservadoramente dado antigo.
- Consequência honesta: o **atalho zero-HTTP `AllMirrorsKnownStale`** (todos confirmados velhos → adiar sem nenhuma requisição nem reserva) continua **não podendo disparar hoje**: de/kumi falham por infraestrutura, não por idade confirmada. A prova completa dele segue pendente de uma janela com espelhos respondendo (a renovação de cota às 00:00 UTC de 24/10 é a próxima oportunidade natural). Registrado, não forçado.
- Convenção de timestamp do repositório (Sprint 69) em ação, sem mudança: o JSON expõe `ObservedAtUtc` com o deslocamento uniforme de sessão (servidor grava a parede 14:24:01; leitura devolve 17:24:01Z). Diferenças permanecem corretas; o valor bruto no banco é o escrito.

## Operação

Worker parado durante o rebuild e reiniciado com o binário desta Sprint: supervisor 32436, worker PID 52100, processo único, heartbeat `running`, migrações íntegras (51). Nenhuma senha, MFA, cota ou envio de e-mail alterado.

## Encerramento operacional

As duas superfícies de operação dizem a mesma coisa com a mesma regra: fila + motivos no SSH, espelhos + motivos no navegador — e a regra que decide "antigo/fresco/ilegível" mora num único lugar com teste de unidade. O custo de encontrar os dois defeitos (a NRE que só bacia quando o espelho mente a data, e as cores que nunca existiram) foi exatamente o preço de um teste novo que olha o HTML de verdade.
