# Sprint 75 — a pesquisa do Worker ganha pools próprios: a Cuia acorda com cota cheia todo dia

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

Inspeção de rotina na manhã após a Sprint 74 (~24 h de operação contínua + renovação de cotas à meia-noite UTC). O Worker estava saudável — zero erros no log, mesmo PID desde a véspera, ciclos normais de discovery/crawler/coleta culinária — e a geocodificação avançou de verdade (286 → 306 coordenadas). O espelho Overpass continua parado, como de costume. Mas havia um número que não combinava: **`drael_search` já estava 20/20 às 00:15** e `public_site_search` **30/30 às 00:20** — o dia inteiro de cota interativa consumido antes de qualquer pergunta à Cuia. O cruzamento com o histórico revelou o padrão exato: ontem, com o binário antigo, `cuia_drael` chegou a 20/20 às 00:18; hoje, com o binário novo, `drael_search` chegou a 20/20 às 00:15. O Sprint 73 separou geração de busca, mas deixou os dois processos — Worker e Web — brigando pelo mesmo pool de busca. O bug que o usuário já tinha visto uma vez teve a segunda vida.

## O diagnóstico (com números)

1. **Quem consumiu:** `research_jobs` (`run_day=2026-10-24`) registra **152 jobs concluídos entre 00:00:50 e 01:32:28** — 15 `ok` (com fontes reais), **132 `quota_exhausted`**, 4 `unavailable`, 1 `empty`. A pesquisa contínua do Worker (`ResearchHostedService`, polling de 60 s, lote 2 por ciclo, habilitada em user-secrets) drenou o pool inteiro nos primeiros ~15 minutos e passou a noite inteira marcando falha honesta job por job.
2. **Por que sempre vai recomeçar:** há exatamente **152 `discovery_targets` habilitados** (= 1 job/dia por alvo, via `INSERT IGNORE` por `run_day`). Cada dia é uma nova leva completa; sem intervenção, todo dia 00:0x o pool interativo renasce zerado para o Worker.
3. **O que não foi afetado:** o ledger de geração (`ai_cost_entries`) ficou intacto na madrugada — apenas os pools de busca e de sites públicos foram consumidos.
4. **Acheado colateral:** `public_site_search` batia em **30/30 todos os dias desde 10/20** (mês: 150/300 — teria esgotado o teto mensal por volta de 10/31). O pool isolado do Worker precisa do teto mensal próprio maior.
5. **Nota de investigação:** os logs locais do Worker são UTF-8, mas PowerShell 5.1 lê como ANSI — buscas por palavras acentuadas ("Pesquisa contínua") falham silenciosamente; padrão ASCII ("Pesquisa") resolveu.

## O que mudou

- **Nome de cota configurável por braço:** `DraelOptions.SearchQuotaService` (default `drael_search`) + tetos próprios da busca `SearchDailyRequestLimit = 20` / `SearchMonthlyRequestLimit = 200` (separados dos tetos de geração). `DraelWebSearch` reserva o serviço configurado.
- **Mesmo ajuste nos outros braços:** `PublicSiteSearchOptions.QuotaService` (default `public_site_search`) e `PublicSourcesOptions.QuotaService` (default `wikipedia_search`).
- **Roteamento do Worker em appsettings versionado** (não é segredo): pesquisa contínua reserva em `worker_drael_search` (diário **10** — metade do teto interativo: mantém o dia da conta ≤ 30 chamadas, e a cobertura é comparável, pois antes só ~13% dos 152 jobs/dia eram atendidos; agora ~6,6% rotacionam por `next_run_at`) e `worker_public_site_search` (mensal 900 — o sweep legítima 30/dia; 300/mês matava em ~10 dias).
- **`--verify-sprint48`** passa a listar os serviços novos no resumo do livro-razão.
- **Testes** (`Sprint75PoolIsolationTests`, 5 fatos): defaults preservam os nomes interativos; o braço drael respeita pool + tetos configurados; public-sites respeita; wikipedia respeita; e um fato de fiação que lê o `appsettings.json` do Worker e garante que `worker_*` segue apontando onde deve (regressão de configuração viraria erro de teste, não surpresa operacional).

## O que a validação revelou — um bug latente que quase virou bode expiatório

Durante a validação, `CuiaAiEndpointTests.Ask_WithValidToken_ReturnsJson` caiu duas vezes seguidas — e **caiu igual na árvore S74 intacta** (`git stash` + rebuild), inocentando esta Sprint na hora. Mecanismo: os user-secrets locais alimentam o **Gemini real** na suíte (o ledger registrou entradas `cuia_concierge` de minhas próprias execuções), e o teste espera a frase determinística "Não encontramos restaurantes conferidos" quando não há recomendações. Com o pool de Gemini zerado (fim do dia) o fallback determinístico responde e o teste passa; com o pool **limpo** (logo após a meia-noite) o modelo responde à vontade e a frase não aparece. No CI não há secrets → sempre determinístico → sempre verde. Era um teste que piscava todo santo dia à meia-noite. Correção: o factory da classe nula as quatro chaves de AI (hermeticidade igual ao CI), sem afetar nenhum outro teste da classe (nenhum depende de modelo real — verificado). Após o fix: **duas suítes completas verdes consecutivas**. Registro honesto: uma execução completa intermediária teve 1 falha transitória não reproduzida nas duas seguintes.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **535/535** (base 530 + 5 fatos de pools isolados).
- Integração local: **246 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas.
- Ao vivo (detalhe em [`artifacts/sprint75-live-evidence.txt`](../artifacts/sprint75-live-evidence.txt)): `--probe-public-search` com a mesma fiação DI do serviço hospedado devolveu `Status="ok"` com **5 fontes reais** (Rota Combu, Terra, ilhadocombu.tur.br, Esse Mundo é Nosso, Viaje Leve) em 6,9 s; o livro-razão mostra as reservas caindo em **`worker_drael_search`** e **`worker_public_site_search`** enquanto `drael_search` (20 @ 00:15) e `public_site_search` (30 @ 00:20) ficaram **idênticos ao byte** — a separação provada não por convenção, mas por nome.

## O que está provado agora — e o que ainda espera (posição honesta)

- **Provado:** a pesquisa do Worker reserva em pools isolados pela fiação real (CLI sobre o host de produção); os pools interativos são estruturalmente intocáveis; defaults preservam o comportamento da Web sem config.
- **Ainda espera (externo):** a primeira ocorrência natural fica para a varredura das 00:00 de 25/10 — os jobs de amanhã vão escrever em `worker_*`; o espelho Overpass segue parado (Sprint 72); o teto de 10/dia do Worker continua deixando ~93% dos jobs honestamente `quota_exhausted` (já acontecia com 20/dia) — a rotação por `next_run_at` distribui cobertura entre cidades/categorias dia a dia.
- **Limite declarado:** a hermeticidade aplicada cobre a classe `CuiaAiEndpointTests`; outras classes da suíte podem ainda sentir o Gemini real (nenhuma sentiu nesta rodada) — se sentir, a receita agora tem endereço.

## Operação

Worker parado durante o rebuild e toda a validação; o probe rodou como processo único e efêmero antes do restart; Worker reiniciado com o binário desta Sprint (supervisor novo, estado `running`, processo único verificado via CIM). A chave `ci-log` do CI mantém seu contrato. Nenhuma senha, MFA ou envio de e-mail alterado; evidência gitignoreada entra com `git add -f`.

## Encerramento operacional

A virada de cota à meia-noite era motivo de comemoração — até perceber que ela era comida antes de o dia abrir. O padrão do Sprint 73 (um pool dedicado por preocupação) aplicado agora na fronteira de processos: cada processo paga da própria conta, e a promessa para o público — a Cuia responde com fontes — deixa de depender de ninguém acordar primeiro. A varredura continua fazendo seu trabalho à noite; só agora trabalha sem tomar o café de quem atende de dia.