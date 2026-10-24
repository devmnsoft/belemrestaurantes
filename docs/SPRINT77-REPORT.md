# Sprint 77 — compartilhar a ficha é a ficha: og:url canônico + JSON-LD Restaurant sem invenção

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

O Sprint 76 entregou o mapa para o buscador; o Sprint 77 cuida do outro caminho de tráfego — **o link compartilhado**. O diagnóstico honesto da ficha pública (16.162 páginas): o `_Layout` já emitia `og:title`/`og:description`/`twitter:card` por página (infraestrutura da era S73), mas três defeitos faziam o card de pré-visualização chegar mal:

1. **`og:url` saía vazio** — o layout renderiza `<meta property="og:url" />` sem conteúdo porque nenhuma view de estabelecimento preenchia a chave (só a de experiência preenchia);
2. **`og:image` podia ser relativo** — qualquer ficha com foto em `/media/photo/...` publicava URL relativa, e raspador que não resolve relativa descarta a imagem;
3. **sem JSON-LD na ficha** — receita já publicava schema.org/Recipe; as 16 mil fichas de estabelecimento não publicavam *nada* estruturado para o Google (sem `Restaurant`, sem endereço estruturado, sem telefone, sem geo).

As páginas de prato, ocasião e roteiro detalhe também não setavam nenhum `og:*`. Não há chave nova de configuração: tudo reusa `Seo:PublicOrigin` do Sprint 76 (mesmo resolvedor, mesmo default oficial, mesma regra HTTPS obrigatória).

## O que mudou

- **`RestaurantJsonLd`** (Application, função pura): monta o grafo schema.org/Restaurant a partir do `RestaurantView` já servido pelo controller — testável sem banco nem HTTP. Regra de honestidade da casa aplicada ao schema: `image`, `address`, `telephone`, `sameAs` e `geo` **só existem quando o dado existe na ficha**; coordenada só entra se o par está dentro dos limites válidos (|lat| ≤ 90, |lng| ≤ 180); lat/lng viram texto com ponto decimal invariante (buscador não chora vírgula de pt-BR).
- **`AbsolutePhotoUrl`**: aceita exatamente as duas formas que a vitrine já aceita — relativo sob `/media/` (ganha a origem configurada) ou absoluta HTTPS (passa crua); qualquer outra forma é tratada como "sem foto", nunca adivinhada. É o mesmo predicado do hero da view, então o que aparece como `og:image` é literalmente o que aparece na página.
- **`Establishment/Details.cshtml`**: passa a setar `OgUrl` sempre (absoluto na origem), `OgImage` só quando há foto pública absoluta, e publica o bloco `<script type="application/ld+json">` com o grafo — mesmo padrão visual da receita.
- **Detalhes de prato/ocasião e de roteiro**: ganham `Title`, `og:type=article`, `og:title/description` do conteúdo editorial existente e `og:url` canônico na origem configurada (rota exata que o sitemap usa: `/pratos/{slug}`, `/ocasioes/{slug}`, `/roteiros/{slug}`).

## Achados da investigação (com números)

1. **Instâncias Web do S76 ainda vivas durante o debugging:** três pares de processos (`dotnet run` + apphost) sobravam das sessões anteriores e seguravam portas — dois deles servindo o binário **Debug** de 12:24 no lugar do Release novo, o que disfarçou a correção por uma hora. Limpeza final por filtragem de linha de comando no CIM; lição operacional registrada: instância de teste morre junto com a sprint que a levantou.
2. **Razor escapa `+` em atributos:** o bloco renderiza como `<script type="application/ld&#x2B;json">` — HTML válido (o navegador decodifica a entidade antes do parser JSON-LD), mas asserção por substring literal falha. O extrator de teste busca o início do bloco sem depender do sufixo.
3. **`/estabelecimento/{id}` já tinha `Cache-Control: private,no-store`** no controller desde antes da ativação do middleware (S76): a ficha nunca precisará de vary-by-cookies, e o cache de resposta não interfere nela.

## Testes

- **Unidade** — `Sprint77RestaurantJsonLdTests` (6 fatos): grafo completo carrega todas as chaves com valores exatos (incluindo lat/lng em texto invariante); ficha nua gera exatamente 4 chaves e nada mais; geo exige o par dentro do mundo; `AbsolutePhotoUrl` aceita as duas formas públicas e rejeta o resto (caminho relativo sem `/media/`, HTTP externo); URL da página absoluta na origem; serialização válida com `@type=Restaurant`.
- **Integração** — `Sprint77SharingTests` (3 fatos, contra o banco local real): ficha publicada semeada com baseline determinístico (foto/telefone/website/coordenadas forçadas a NULL) responde 200 com `og:url` canônico, bloco JSON-LD presente, e **ausência comprovada** de `image`/`telephone`/`sameAs`/`geo`; a mesma ficha enriquecida por UPDATE (foto `/media/...`, telefone, website, coordenadas válidas) passa a exibir `og:image` absoluto e cada chave nova no schema; e as páginas de prato/ocasião/roteiro expõem `og:url` canônico — a escolha do roteiro espelha o predicado do controller (editorial OU paradas), porque roteiro sem os dois é redirecionado ao índice e o teste pegaria a URL errada.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **546/546** (base 540 + 6 fatos novos).
- Integração local: **252 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas.
- Ao vivo (detalhe em [`artifacts/sprint77-live-evidence.txt`](../artifacts/sprint77-live-evidence.txt)): instância Web Release com a origem padrão de produção devolveu, para duas fichas reais — uma com foto externa+telefone+endereço e outra sem foto mas com coordenadas — exatamente o que o banco carrega: `og:url`/`og:image` absolutos, `Restaurant` JSON-LD com `image`/`address`/`telephone` presentes quando existem e ausentes quando não existem, `geo` com o par correto; páginas `/pratos/tacaca` e `/roteiros/cidade-velha-sem-pressa` com `og:url` canônico.

## Operação

Worker parado durante os rebuilds e reiniciado com o binário desta Sprint (supervisor 228 / worker 17188, estado `running`, processo único verificado via CIM). Instância Web de teste levantada apenas para a evidência e derrubada em seguida (junto das três sobras do S76). Evidência gitignoreada entra com `git add -f`. Nenhuma senha, MFA ou envio de e-mail alterado.

## Encerramento operacional

O Google agora tem o mapa (S76); quem manda o link no WhatsApp agora recebe a prévia de verdade (S77) — título, descrição, foto absoluta quando ela existe e um schema que o buscador entende, dizendo exatamente o que a ficha sabe: nada a menos, nada a mais. Dezesseis mil fichas passaram a ser compartilháveis como deveriam desde o primeiro dia.
