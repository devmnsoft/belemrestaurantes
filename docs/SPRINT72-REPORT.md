# Sprint 72 — o atalho zero-HTTP da Sprint 67 ganha a prova que faltava: o ciclo inteiro, com fila e cota de verdade

Execução local em 23/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

A Sprint 67 prometeu o elo mais caro do pipeline de geocodificação: quando **todos** os espelhos Overpass estão confirmados com dado mais velho que 14 dias dentro da janela (60 min), o ciclo adia a ficha **antes** de reservar unidade do teto diário — zero requisição, zero cota. A mecânica da fonte já era prova-de-unidade desde então (`TwoMirrors_BothConfirmedStaleInTwoCalls_ThirdCostsNothing` etc.), e a fiação do host existe no código (`GeocodingHostedService`, linha "if `AllMirrorsKnownStale` → adiar senão reservar") — mas esse caminho **nunca tinha rodado num teste com fila, reserva e banco reais**. Ao vivo ele nem podia disparar: `overpass-api.de` e `overpass.kumi.systems` falham por infraestrutura (não confirmam idade), e o único espelho que responde (`osm.ch`) carrega snapshot de idade ilegível. Faltava fechar o único elo sem prova — e, aproveitando a janela, avaliar se valia a pena ampliar a redundância de espelhos públicos enquanto eles não voltam.

## O que mudou

- **`Sprint72StaleMirrorZeroCostCycleTests`** (integração, coleção MySql, 2 fatos) — o primeiro teste que chama `GeocodingHostedService.RunAsync` (a mesma entrada do `--geocode-pending`) com a montagem real de DI do Worker + sobrescritas de última hora (o último registro vence): Nominatim vira stub determinístico, o cliente HTTP seguro vira contador (tudo que atravessa `ISafeHttpClient` ali é tráfego Overpass, porque o stub corta o Nominatim na raiz), e os três espelhos viram URLs `.test` únicas por execução (estado estático de frescor isolado por token).
  - **Fato 1 — o atalho:** três espelhos confirmados velhos antes do ciclo; a ficha-fixture entra na fila com pool pré-cacheado (Sprint 66). Resultado medido no banco: ficha processada e adiada como `overpass_stale_data`/`pending`, **0 requisição Overpass no contador, 0 chamada ao Nominatim (replay de cache), Δgranted = 0**. O "zero unidade" não é inferido: é a diferença exata do contador diário antes/depois.
  - **Fato 2 — o controle:** só dois espelhos confirmados. O ciclo reserva e consulta **exatamente uma vez o espelho desconhecido** (que responde velho-e-vazio), zero aos confirmados, Δgranted = exatamente +2 (Nominatim sem cache + Overpass), e a ficha ainda assim adia em `overpass_stale_data` — resposta velha confirma, não rejeita. É o contrafactual que impede o fato 1 de ser explicável por "o ciclo só adia".
- **Referência `IntegrationTests → BelemRestaurantes.Worker`:** o Worker não referencia nem Web nem nenhum projeto de teste (sem ciclo); o `appsettings.json` do Worker é item `None` (não propaga para a saída dos testes, ao contrário do SDK Web) — validado pelo build 0/0.
- **Içamento reutilizável de teste de ciclo** (documentado no próprio arquivo para a próxima vez): batch de 1 + endereço tokenizado únicos; isolamento da fila compartilhada sem destruir a produção local — as pendências vencidas têm `next_run_at` **fixado em agora+1 h** com captura exata numa tabela temporária da conexão e restore literal no `finally`; a fixture é empurrada para agora−1 h e vira a única vencida (`ClaimAsync` ordena por `next_run_at, restaurant_id`); efeito colateral dos stubs limpo por `last_status='stub_out_of_scope'` (status único do teste).

## O que a validação revelou

1. **A fila de desenvolvimento esconde uma armadilha de teste que o primeiro vermelho expôs.** O banco local tem ~17 mil pendências vencidas, e uma coorte inteira carrega `next_run_at` com **dias** de atraso (ex.: `2026-10-22 15:56`). O instinto óbvio — "somar 1 h às vencidas" — não tira da fila quem já estava mais de 1 h atrasado: cada execução depurativa consumia uma ficha real (`1096` chegou a `failed/stub_out_of_scope/a4` nos runs intermediários). A correção foi alvo absoluto (agora+1 h) + restore exato, e o dano colateral foi revertido por completo (as linhas com status do stub foram removidas; essas fichas voltam à fila no próximo ciclo natural do Worker, exatamente como voltariam sozinhas). Lição geral: num banco compartilhado, "empurrar para o futuro" precisa ser **absoluto** e auditável, e a asserção do UPDATE (linhas afetadas = 1) entrou no teste por causa disso.
2. **Dois vícios do xunit 2.9.3 para o caderno do repositório:** `Assert.Equal(int, int, string)` não compila (o candidato vencedor da resolução de sobrecarga quer `Func<int,int,bool>` — CS1503), e `Assert.NotNull(object, string)` não existe (CS1501); a forma que aceita mensagem nos dois casos é `Assert.True(cond, msg)`. E comentário XML em `.csproj` não comporta `--` (o `--geocode-pending` dentro de `<!-- -->` quebra o MSBuild).

## Validação

- Build Release `--no-incremental` (solução completa, worker parado): **0 avisos / 0 erros**.
- Unidade: **528/528**.
- Integração local completa: **242 aprovados / 1 ignorado (verificação opcional de credenciais, preexistente) / 0 falhas** — 240 da base + os 2 novos. Registro honesto: a primeira tentativa da suíte completa saiu com código 1 sem nenhum teste falho na saída (aborto em nível de host, padrão desconhecido); a reexecução integral imediata passou em 2 m 25 s, incluindo os novos fatos.
- Estado pós-execução confirmado no banco: contagens da fila restauradas (as pendências captadas voltaram aos valores originais), zero resíduo de cache/frescor com token S72, `overpass_mirror_freshness` de volta à única linha real (osm.ch).

## O que está provado agora — e o que ainda espera (posição honesta)

- **Prova concluída:** a fiação "verifica antes de reservar" no nível do ciclo, com MySQL real: o Δdo contador diário mede zero no atalho e exatamente +2 no controle; o contador HTTP mede zero requisição no atalho e exatamente uma ao espelho desconhecido no controle; espelhos confirmados velhos são pulados até dentro do failover; resposta velha-e-vazia adia (não termina). Somada à prova-de-unidade da mecânica (S67) e à corroboracão em processo longevo (S70), a cadeia S67 ficou sem elo sem teste.
- **Ainda espera (externo ao nosso controle):** a demonstração ao vivo com os espelhos públicos — exige que de/kumi respondam para confirmar idade. Sondas de hoje (~18:4x UTC): `de` → erro TLS (~0,7 s, mudou do 406 de ontem, mesma categoria: infraestrutura), `kumi` → timeout (25 s), `osm.ch` → HTTP 200 com `timestamp_osm_base='117943'` ilegível → conservadoramente dado antigo. A próxima janela natural segue sendo a renovação de cota às 00:00 UTC de 24/10.
- **Redundância de espelhos avaliada e arquivada hoje:** `overpass.openstreetmap.be` (DNS), `overpass.private.coffee` (timeout), `maps.mail.ru/osm/tools/overpass` (504), `overpass.osm.jp` (confiança TLS) — nenhum utilizável a partir desta rede em ~18:4x UTC. Decisão por evidência: configuração de failover permanece `[de, kumi, osm.ch]`; o ganho de adicionar um quarto só aparece quando um candidato responder de fato com dado legível.

## Operação

Worker parado durante o rebuild e toda a validação; reiniciado com o binário desta Sprint ao final (detalhe de processo no encerramento). Nenhuma senha, MFA, cota ou envio de e-mail alterado. A chave `ci-log` do CI continua com seu contrato de sempre (transitória por falha, apagada ao verde).

## Encerramento operacional

O argumento da Sprint 67 — "adiar barato quando o mundo inteiro disse que está velho" — deixou de ser propriedade de uma função isolada: é comportamento do sistema, medido em duas grandezas físicas (requisições e unidades) num contrafactual que exclui a alternativa trivial. O que sobrou na mesa não é trabalho: é paciência (espelhos externos e relógio UTC). E, de brinde, a fila de desenvolvimento ganhou seu primeiro procedimento de isolamento documentado e reutilizável para o dia em que outro ciclo precisar de prova igual.
