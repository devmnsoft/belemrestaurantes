# Sprint 79 — os catálogos têm cartão próprio: as páginas de cidade, localidade e categoria ganham compartilhamento honesto (e as categorias que estavam zeradas voltaram a responder)

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

O arco S75–S77 deixou a camada de SEO/conteúdo pronta para as **fichas de detalhe** (estabelecimento, prato, ocasião, roteiro, receita com JSON-LD). Mas o sitemap indexa **seis** famílias de URL, e três delas — `/cidade/{slug}` (144 cidades), `/localidade/{slug}` (zonas turísticas) e `/categoria/{slug}` (categorias com milhares de publicações) — navegam sem um único `og:`: quem compartilha um link da página de Santarém no WhatsApp recebe o cartão genérico da home. E `/receitas/{slug}`, embora já tivesse `og:title`/`og:description`/JSON-LD, **não definia `OgUrl`** — o layout imprimia `<meta property="og:url" content="">` vazio, que raspadores tratam como defeito.

A implementação abriu uma caixa de surpresa: ao escrever a descrição honesta ("N estabelecimentos publicados…"), ficou impossível não ver que **/categoria/mercearia respondia "Nenhum estabelecimento publicado nesta categoria" com 4348 publicações guardadas sob esse rótulo**. A busca estava **normalizando duas vezes**: `CategoryRules.Clean` já converte a categoria na ingestão (entidade, Google Places, IA), e o repositório de leitura a reaplicava na hora da busca com o dicionário **evoluído desde então** — 'Mercearia' virava 'Comércio alimentício', um valor canônico que não existe como rótulo guardado → igualdade exata → zero linhas. O mesmo defeito zombava o dropdown de categorias do `/busca`.

## O que mudou

- **`CatalogPageSeo`** (Application, função pura): a identidade compartilhável das páginas de catálogo. `OgUrl` = origem pública + caminho servido **sem consulta** (paginação aponta sempre para a primeira página; query no canônico é exceção); `Title`/`ShareTitle` por tipo (cidade/localidade: "Restaurantes em {lugar}"; categoria: "{rótulo} no Pará"); `Description` construída **apenas com a contagem real** da renderização — zero tem frase própria, plural segue o número, dígitos em cultura invariante para leitura determinística por raspadores.
- **`Search/Index.cshtml`** (a view única que serve `/busca` + os três catálogos): bloco `og:` acionado pelo contexto (cidade / localidade / categoria) com título, meta description, og:title/description/url próprios.
- **`SearchController.CategoryPage`**: quando o slug casa com um rótulo guardado no catálogo, exibe **o rótulo como o catálogo o escreveu** ("Loja de Alimentos", não "loja de alimentos") — a busca é insensível a maiúsculas/acentos, então a troca não muda o resultado.
- **`MySqlRestaurantReadRepository.SearchAsync`**: o filtro estruturado de categoria compara agora o rótulo guardado diretamente (trim + colação) **sem re-canonicalizar** — reverte a dupla normalização que zerava a busca.
- **`Recipes/Details.cshtml`**: `OgUrl` canônico `/receitas/{slug}` na origem pública. Sem fotografia real, a receita continua sem prometer `og:image`.
- **`_Layout`**: `twitter:card` só afirma `summary_large_image` quando existe `og:image`; sem imagem desce para `summary`. Fim da promessa de "imagem grande" que a maioria dos cartões cumpria com o nada.

## Achados (com números)

1. **As categorias mais povoadas do estado respondiam vazias.** Antes do ajuste, `/categoria/mercearia` renderizava o estado zero com **4348** publicações guardadas como "Mercearia", e `/categoria/loja-de-alimentos` também zerada com **1208** guardadas. Causa raiz registrada acima: o dicionário de canonicalização evoluiu após a ingestão das linhas, e a releitura re-aplicou o dicionário novo sobre valores antigos. A correção é de leitura (não migra dados): o valor guardado é a verdade do catálogo.
2. **O bug era visível no `/busca` inteiro**, não só nas rotas: selecionar "Mercearia" no filtro de categorias fazia o mesmo caminho de dupla normalização.
3. **Lição operacional, pela segunda vez, registrada com nome:** uma instância Web sobrevivente da evidência da S77 (pid 16432) ainda segurava a porta 63870 durante as capturas desta Sprint — mesmo padrão da S77 (instâncias de teste morrem com a Sprint que as subiu; conferir `Get-NetTCPConnection` por porta antes de culpar o código). Bônus do incidente: descobriu-se que a ferramenta de shell deste ambiente desmonta argumento nativo `"url" + $var` — em chamadas curl, interpolar (`"url$var"`) é o que chega intacto ao servidor.

## Testes

- **Unidade** — `Sprint79CatalogPageSeoTests` (15 fatos): títulos e share titles dos três tipos; tipo desconhecido falha alto; descrições nos ramos 0/1/N com o número exato em cultura invariante ("2924 restaurantes publicados em Belém.", "1208 estabelecimentos publicados como loja de alimentos."); contagem negativa rejeitada; `OgUrl` exige HTTPS, tolera barra final na origem e **rejeita query e caminho relativo**.
- **Integração** — `Sprint79CatalogSharingTests` (5 fatos, contra o banco real): a página de Belém traz o cartão canônico com a contagem lida do banco no momento do teste (mesma função pura do serviço, nada codificado no assert); a paginação **não muda** a URL canônica; a localidade Praia do Atalaia aponta para sua própria URL com contagem real; a categoria Loja de Alimentos exibe o rótulo guardado e o exato número publicado; a receita carrega `og:url` canônico e `twitter:card=summary` honesto.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **561/561** (base 546 + 15 novos).
- Integração local: **259 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas, sem regressão na mudança do filtro de categoria.
- Ao vivo (detalhe em [`artifacts/sprint79-live-evidence.txt`](../artifacts/sprint79-live-evidence.txt)): seis URLs capturadas na origem oficial padrão — as contagens renderizadas batem **exatas** com o banco (Belém 2924, Praia do Atalaia 4, Mercearia 4348, Loja de Alimentos 1208), a página 2 repete o canônico da página 1, e a receita fecha com `twitter:card=summary`.

## Operação

Worker reiniciado com o binário desta Sprint (supervisor 53128 / worker 52236, estado `running`, processo único verificado). A instância Web usada nas capturas foi derrubada depois da evidência (porta 63870 livre, zero processos Web restantes). Evidência gitignoreada entra com `git add -f`. Nenhuma senha, MFA ou envio de e-mail alterado.

## Encerramento operacional

Cada família do sitemap agora se apresenta pelo próprio nome e com o próprio número: a cidade compartilha "2924 restaurantes publicados em Belém.", a categoria publica quantos são, a receita aponta para si mesma, e a categoria que respondia "nenhum" com milhares guardados voltou a dizer a verdade — o rótulo guardado é a verdade do catálogo, e a busca voltou a tratá-lo assim.
