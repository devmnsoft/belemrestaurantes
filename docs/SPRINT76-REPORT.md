# Sprint 76 — o portal ganha mapa para o mundo: /robots.txt + /sitemap.xml sem invenção

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

O portal tem **16.162 páginas publicadas** que buscador nenhum tem como descobrir: não havia `robots.txt`, não havia `sitemap.xml`. O detalhe mais honesto do diagnóstico: o seed do banco já carregava uma linha em `discovery_targets` apontando para `https://belemrestaurantes.com.br/sitemap.xml` — **nosso próprio crawler configurado para raspar um sitemap nosso que era 404**. O resto da casa de indexação já existia e só não era usado: o `_Layout` tem os ganchos `og:title`/`og:image`/`twitter:card` por página, receitas já publicam JSON-LD schema.org — faltava o mecanismo de descoberta em si.

## O que mudou

- **`SeoController`** (Web): `GET /robots.txt` (text/plain, `Disallow` para `/admin`, `/api`, `/account`, `/partner`, `/language`, `/dev` + declaração `Sitemap:`) e `GET /sitemap.xml` (application/xml, namespace oficial `sitemap/0.9`). A origem absoluta vem de `Seo:PublicOrigin`, com padrão na origem oficial `https://belemrestaurantes.com.br` e **HTTPS obrigatório** — a mesma regra que já governa os links transacionais dos membros (`MemberAccounts:PublicOrigin`).
- **`SitemapBuilder`** (Application, função pura): `<loc>` sempre absoluto; `<lastmod>` só quando conhecido, no subconjunto W3C (UTC com `Z`, fuso normalizado); escapamento XML dos 5 caracteres especiais; teto prático de **50.000 URLs por arquivo** — acima disso falha alto pedindo sitemap index, nunca truncando em silêncio.
- **`ISitemapSource` / `MySqlSitemapSource`**: as seções dinâmicas listam **exatamente o que responde no portal**, com o mesmo predicado das rotas: estabelecimentos publicados (paginação por chave de mil em mil, padrão da exportação aberta), receitas publicadas, roteiros ativos+publicados (predicado idêntico ao de `/roteiros/{slug}`), municípios, localidades turísticas e categorias que existem entre os publicados. Estabelecimento pendente não aparece.
- **23 rotas estáticas** em constante pública do controller — e um teste de integração **percorre todas elas exigindo 200**: rota quebrada quebra o build, não o sitemap em produção.
- **Ativação real do cache de resposta:** o middleware `AddResponseCaching`/`UseResponseCaching` (nome renomeado no .NET 10 — o antigo `AddResponseCache` sumiu do shared framework) **nunca tinha sido registrado**; todos os `[ResponseCache]` do app — incluindo a exportação ODbL da Sprint 73 — eram inertes. Agora: `Cache-Control: public,max-age=900` sai de verdade e a réplica servida de memória responde em ttfb de 5–35 ms (vs. ~4 s sem cache).

## Achados da investigação (com números)

1. **Municípios duplicados no mapa do estado:** `geo_municipalities` carrega **4 pares** com o mesmo nome e **IBGE divergente** (Salvaterra 1506302/1506304; Santa Bárbara 1506351/1506544; Santa Izabel 1506353/1506500; Santa Maria 1506585/1506609 — seeded em 17/10). O sitemap deduplica por slug (`DISTINCT` sobre a mesma transformação dos links internos): uma URL por lugar. A higienização da tabela fica registrada como pendência, fora do escopo desta Sprint.
2. **Primeira reconciliação quebra por duplicata:** a contagem exata apontou 4 URLs repetidas — exatamente os pares acima; nenhum outro tipo de entrada duplica (verificado por `GROUP BY loc` sobre todas as seções).
3. **Medição honesta de latência:** o `Invoke-WebRequest` do PowerShell 5.1 reportava 2,6–4,3 s "servindo" o sitemap; o `curl` (ttfb) mostrou 5–35 ms — o tempo extra era deserialização client-side de 1,9 MB no .NET Framework, não o servidor. O artefato registra as duas leituras e qual vale.

## Testes

- **Unidade** — `Sprint76SitemapTests` (5 fatos): urlset válido com namespace oficial e `lastmod` opcional/fuso normalizado para `Z`; escapamento de `& < > "`; teto por protocolo falha com mensagem acionável; origem padrão oficial e HTTPS obrigatório; robots declara o sitemap e protege as regiões privadas.
- **Integração** — `Sprint76SeoEndpointTests` (3 fatos): contrato do `robots.txt` (content-type, Disallow, `Sitemap:` na origem configurada); **sitemap reconciliado seção por seção com SQL direto** (semeia um restaurante pendente e um aprovado: o aprovado entra com `lastmod`, o pendente nem aparece; URLs únicas e absolutas; totais batem contagem exata); todo caminho estático do sitemap responde 200.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **540/540** (base 535 + 5 fatos novos).
- Integração local: **249 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas.
- Ao vivo (detalhe em [`artifacts/sprint76-live-evidence.txt`](../artifacts/sprint76-live-evidence.txt)): instância Web na porta 63861 devolveu `/robots.txt` com corpo exato e `/sitemap.xml` com **16.424 entradas reconciliadas seção por seção** — 16.162 estabelecimentos publicados (= o total do banco, sem resto), 12 receitas, 12 roteiros, 144 cidades (já deduplicadas), 29 localidades, 26 categorias, 10 pratos, 6 ocasiões, 23 hubs — com `Cache-Control: public,max-age=900` e ttfb ≤ 35 ms nas chamadas seguintes.

## Operação

Worker parado durante os rebuilds e reiniciado com o binário desta Sprint (supervisor 17692 / worker 36372, estado `running`, processo único verificado via CIM). Instância Web de teste levantada apenas para a evidência e derrubada em seguida. Evidência gitignoreada entra com `git add -f`. Nenhuma senha, MFA ou envio de e-mail alterado.

## Encerramento operacional

Dezesseis mil páginas existiam sem mapa — inclusive um mapa que nosso próprio crawler já tentava raspar no ar. O sitemap agora obedece à regra da casa: o que entra nele responde 200 no portal, e o que não existe nem aparece. Descoberta deixou de depender de sorte: quem procura Belém, acha a porta certa.
