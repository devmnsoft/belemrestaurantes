# Sprint 80 — os idiomas do site têm endereço: `?lang=xx` resolve a língua e cada página anuncia as versões que existem

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

O arco S76–S79 deixou cada página indexável e compartilhável — mas os idiomas do site só existiam por *inferência*: a cultura caía no cabeçalho `Accept-Language`, ou seja, um navegador com preferência inglesa via o site em inglês **no mesmo endereço** da versão portuguesa, sem nenhum rótulo dizendo isso. Consequências concretas: buscadores não sabiam a língua de cada URL (e o Googlebot, que se apresenta em inglês, recebia conteúdo inglês em URLs sem nenhum indício); um link compartilhado em inglês apontava canonicamente para a página PT; e trocar de idioma exigia `POST` + cookie — algo que rastreador nenhum faz.

A checagem de dados antes do escopo foi honesta e restritiva: os oito `SharedResource.*.resx` carregam 364 strings cada, mas **somente pt-BR, en e es têm tradução própria verificada** (amostras: "O Pará à flor do paladar" / "Pará, a taste of the Amazon" / "Pará, un viaje de sabores"); fr, de, ja, ru e zh-Hans mantêm o fallback editorial inglês que o site já informa ao visitante. Declarar tradução onde não há seria inventar.

## O que mudou

- **`QueryLanguageProvider`** (Web): o parâmetro `?lang=xx` escolhe o idioma de qualquer página pública, sem cookie nem clique. Ordem de prioridade: **cookie do usuário** (escolha persistida) > **parâmetro da URL** (endereço publicado) > **Accept-Language** (preferência do navegador, comportamento anterior preservado) > padrão pt-BR. Somente códigos oficialmente suportados são aceitos (sem sensibilidade a maiúsculas); valor desconhecido é ignorado em silêncio.
- **`HreflangBuilder`** (Application, função pura): o conjunto traduzido é fixo (`pt-BR`, `en`, `es`); `x-default` aponta para a página de registro (PT, sem parâmetro); cada idioma não-padrão aponta para a **mesma** página com `?lang=xx`; `og:url` fica auto-referente por cultura (sufixo uma única vez, idempotente); origem absoluta com a mesma régua HTTPS do sitemap; caminho relativo ou com query falha alto.
- **`_Layout`**: toda página pública passa a publicar os quatro `<link rel="alternate">` (pt-br, en, es, x-default) e o `og:url` sai pelo `OgUrlForCulture` — página em inglês compartilhada aponta para si mesma em inglês.
- **Decisão registrada:** o sitemap continua **em língua única** nesta Sprint. Triplicar as ~17 mil URLs levaria o arquivo ao teto de 50 mil — variantes por idioma entram junto com o shard/index quando o volume exigir.

## Achados (com números)

1. **Cinco dos oito resx são cópias do inglês.** A amostra de strings-chave mostrou fr/de/ja/ru/zh-Hans idênticos ao en. O hreflang foi limitado ao conjunto verificado de três: a regra "nunca inventar dados" vale para declaração de idioma tanto quanto para contagem.
2. **Sem risco de colisão com o cache de resposta da S76:** cache com duração existe apenas em endpoints SEO (sitemap/robots) e exportações estáticas; nenhuma página HTML localizada passa por ele, então `?lang=` nunca devolve outra língua em cache.

## Testes

- **Unidade** — `Sprint80HreflangBuilderTests` (17 fatos): forma exata das URLs nos três idiomas; idiomas com fallback único rejeitados (falhar alto em vez de declarar o que não existe); HTTPS obrigatório; caminho absoluto e sem query; conjunto completo na ordem fixa com `x-default` igual à página de registro; `og:url` auto-referente idempotente; vazio rejeitado.
- **Integração** — `Sprint80LanguageAlternatesTests` (5 fatos, hermeticamente): a home publica os quatro alternates na origem absoluta; a página de catálogo em inglês **serve inglês de fato** (`Content-Language: en`, `<html lang="en">`) e aponta para si mesma no `og:url`, mantendo os alternates; `?lang=xx` desconhecido cai em português; o fallback por `Accept-Language` continua funcionando (regra de ordem dos provedores preservada); o sitemap segue sem `?lang=`.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **578/578** (base 561 + 17 novos).
- Integração local: **264 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas.
- Ao vivo (detalhe em [`artifacts/sprint80-live-evidence.txt`](../artifacts/sprint80-live-evidence.txt)): home PT/EN/ES renderiza o título traduzido com os quatro alternates na origem oficial; `/cidade/belem` nas três línguas responde `Content-Language` correto e `og:url` auto-referente (`.../cidade/belem?lang=en` em inglês, canônico sem sufixo em PT); sitemap com a cidade canônica e zero ocorrências de `?lang=`.

## Operação

Worker reiniciado com o binário desta Sprint (supervisor 6408 / worker 21136, estado `running`, processo único verificado). A instância Web usada nas capturas (porta 63870, conferida livre antes de subir) foi derrubada depois da evidência — zero processos Web restantes, porta livre. Evidência gitignoreada entra com `git add -f`. Nenhuma senha, MFA ou envio de e-mail alterado.

## Encerramento operacional

Os idiomas deixaram de ser adivinhação: cada página anuncia exatamente as línguas que existem, `x-default` nomeia a página de registro, e um link em inglês agora aponta para si mesmo em inglês. O próximo degrau registrado é as variantes por idioma no sitemap, junto com a partição em index quando o volume de URLs chegar ao teto.
