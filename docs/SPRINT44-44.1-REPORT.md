# Sprints 44 e 44.1 Mapas e legibilidade

Implementação e homologação local em 19/10/2026. O mapa agora filtra no banco, pagina os resultados e permite explorar pontos com Leaflet carregado após interação. A tipografia de conteúdo parte de 18 px e oferece ampliação até 200%. Há ganho medido na consulta SQL e redução do HTML. A homologação integral das sprints permanece aberta pelos itens externos e manuais descritos ao final.

## Diagnóstico e comparação

O controlador anterior consultava até 500 publicados e só depois filtrava município e termo em memória. Esse corte podia ocultar lugares válidos. As cidades e contagens também vinham desse subconjunto. O SVG era renderizado integralmente no HTML, sem navegação cartográfica. Cuia Rotas já carregava seu iframe após clique, mas não delimitava a espera nem apresentava recuperação de erro.

Chromium headless via Playwright, Windows, aplicação Release em Development, MySQL local, viewport 390 × 844, cinco contextos novos por versão e sem limitação de rede na comparação. Os ensaios compartilham a máquina com compilação/testes, portanto não são uma certificação de desempenho de produção. Medianas dos cinco carregamentos:

| Métrica | Antes | Depois |
| --- | ---: | ---: |
| TTFB | 19,5 ms | 2,9 ms |
| LCP | 360 ms | 164 ms |
| CLS | 0 | 0 |
| HTML decodificado | 44.854 bytes | 27.280 bytes |
| Transferência informada pelo navegador | 236.967 bytes | 228.715 bytes |
| Requisições iniciais | 19 | 20 |
| Pontos no HTML inicial | 57 | 24 |
| Heap informado pelo Chromium | 10 MB | 10 MB |

A redução de HTML inclui a paginação: os outros lugares continuam acessíveis por “Próximos lugares”. O script próprio de cartografia acrescenta uma requisição inicial; Leaflet só é solicitado ao abrir o mapa. O heap é uma leitura arredondada do navegador. Recursos externos sem Timing-Allow-Origin podem ter tamanho transferido zerado.

O primeiro conjunto interativo de pontos ficou disponível **154 ms após o clique** no teste local, sem base de ruas configurada. Isso mede posições e lista, não tiles de produção. A mediana do LCP com rede simulada de 150 ms de latência, 200.000 bytes/s de download e 90.000 bytes/s de upload foi 1.872 ms. Não há comparação anterior nessa rede.

INP não foi amostrado como métrica de campo: o zero no coletor indica ausência de amostras, não INP zero. Três carregamentos finais registraram tarefas longas de 86, 125 e 131 ms, contra nenhuma nos cinco anteriores; não há evidência para afirmar que todos os bloqueios foram eliminados. O relatório bruto contém a duração e o tamanho de cada recurso, incluindo imagens e scripts.

### Consultas com catálogo sintético

O ensaio optativo só aceita banco em loopback, insere dados sintéticos e remove-os em `finally`. Cada cenário acrescenta a quantidade abaixo ao catálogo local, executa 30 amostras e mantém o retorno novo limitado a 200 pontos. Os registros do ensaio ficam concentrados em Santarém; outras distribuições precisam de nova medição.

| Registros acrescentados | Mediana nova | p95 novo | Mediana SQL antigo | p95 antigo |
| --- | ---: | ---: | ---: | ---: |
| 100 | 3,55 ms | 10,88 ms | 4,45 ms | 6,57 ms |
| 1.000 | 5,11 ms | 8,95 ms | 12,20 ms | 15,12 ms |
| 10.000 | 27,45 ms | 52,07 ms | 59,25 ms | 75,64 ms |
| 50.000 | 21,94 ms | 26,09 ms | 230,31 ms | 296,99 ms |

A busca nova inclui a abertura da conexão do repositório, filtros e materialização; a antiga reproduz o SELECT global de 500 em conexão já aberta. São operações com contratos diferentes: a nova resolve a área/filtros, e a antiga ainda exigiria filtragem em memória. O ganho não autoriza extrapolar estes percentis para homologação remota. EXPLAIN escolheu `ix_map_city_cursor` nos cenários menores e PRIMARY nos maiores; os planos estão no JSON do ensaio.

`Server-Timing` expõe o tempo de consulta da API e de preparação do catálogo. O filtro MVC registra a duração de execução do resultado Razor em nível Information. O Meter `BelemRestaurantes.Maps` fornece `map.sql.duration` e `map.routes.duration`, em ms. O tempo SQL instrumentado inclui conexão/materialização; não é medição isolada do motor SQL. A duração real de Google Routes e do iframe exige os serviços externos disponíveis.

## Consultas e API geográfica

`GET /api/mapa/pontos?bbox=-49,-2,-48,0&city=Belém&category=Tacacá&limit=100`

- `bbox` usa oeste,sul,leste,norte em graus; exige números finitos, intervalos válidos e limites crescentes. Caixas que cruzam o antimeridiano devem ser divididas pelo cliente.
- Filtros: `city`, `q`, `category`, `neighborhood`, `tier=ficha|guia`. Textos são limitados a 80 caracteres; SQL usa parâmetros e escapa curingas de LIKE.
- `limit`: 1–200. `after`: cursor de ID, sem OFFSET crescente. `next` indica continuação. O mapa orienta ampliar/filtrar quando há mais pontos; a lista SSR tem paginação própria.
- Somente `published`, coordenadas dentro do intervalo e fora da ilha nula. A resposta contém ID, nome, cidade, bairro, categoria, coordenadas e camada; a ficha mantém a consulta da procedência completa.
- Uma consulta por página de pontos, sem N+1. Agregados são separados, com cache de 30 segundos. Pontos usam Cache-Control público de 15 segundos; não alimentam cache arbitrário em memória do servidor.
- Rate limiting público de 60 requisições/minuto/IP. O cliente usa debounce de 250 ms, AbortController, descarte de respostas obsoletas e cache de até 20 áreas por 15 segundos.

A migração 033 adiciona índices compostos por status/cidade/cursor e status/latitude/longitude/cursor. Usa as colunas decimais existentes; não presume geometria ou SRID de dados legados. Validação em MySQL local; compatibilidade de plano e implantação em MariaDB permanecem para homologação.

## Experiência de mapas e rotas

Leaflet 1.9.4 e sua licença BSD estão em `wwwroot/vendor/leaflet`. Zoom, arraste, marcadores focáveis, agrupamento por proximidade em tela, popups com links seguros, lista equivalente e botão Como chegar funcionam sem bloquear a consulta textual. O agrupamento usa apenas a página recebida, até 200 pontos; não representa uma contagem agregada de todo o catálogo. O SVG inicial permanece como alternativa leve, junto à lista paginada.

O estado de erro permite tentar novamente, preservando a lista. A interface informa quando falta base de ruas. A disposição usa mapa e lista lado a lado em desktop e uma coluna em telas pequenas. O agrupamento mantém distância entre controles, inclusive para pontos coincidentes. Ao abrir o mapa, a lista da área visível substitui os resultados iniciais; “Ver lista” restaura a paginação textual, e erros também a restauram. A lista interativa tem rolagem própria para evitar páginas com centenas de cartões empilhados.

### Configuração de ruas

Não há servidor público de tiles ativado por padrão. Configure `Maps:TileUrl` com um gateway de mesma origem sob `/maps/tiles/`, por exemplo `/maps/tiles/{z}/{x}/{y}.png`, e `Maps:Attribution` com a atribuição exigida pelo contrato. Esse gateway precisa ser fornecido pelo operador da hospedagem/provedor; a aplicação não implementa um proxy de tiles. A atribuição é inserida como texto, sem HTML arbitrário. Chaves privadas devem ficar no gateway, fora do HTML.

O provedor licenciado, capacidade, cache permitido, custos e disponibilidade precisam ser definidos antes de habilitar ruas em produção. A escolha segue a [documentação do Leaflet](https://leafletjs.com/reference.html) e considera a [política dos tiles OSM](https://operations.osmfoundation.org/policies/tiles/).

Google Routes preserva o HttpClientFactory, máscara explícita de campos, reserva transacional de orçamento diário e cancelamento do visitante. A consulta HTTP e leitura do corpo agora têm prazo conjunto de 8 segundos e histograma de duração. Não há repetição automática de chamadas pagas. Timeout, rede ou JSON inválido continuam oferecendo o link do provedor. A seleção de campos segue a [documentação de máscaras do Google](https://developers.google.com/maps/documentation/routes/choose_fields).

O iframe do destino só é criado após clique, tem aviso de carregamento e prazo de 12 segundos. Em erro/timeout, é removido e oferece nova tentativa manual. O evento load entre origens confirma apenas o carregamento do documento; não garante a renderização dos tiles. Endereço, formulário, horário conhecido e links de transporte continuam utilizáveis. Itinerário textual e tarifa só usam os valores retornados; o mapa interno ainda mostra o destino, e o trajeto permanece no planejador externo.

## Legibilidade e contraste

As alterações foram feitas nos estilos dos componentes e nas Views que possuíam tamanhos inline, sem acrescentar outra folha global de sobrescritas. Conteúdos e controles com tamanhos de até 18 px passaram a 1 rem. A raiz é 18 px; os ajustes de 125%, 150%, 175% e 200% correspondem a 22,5, 27, 31,5 e 36 px. Preferências antigas de 115% migram para 125%. Títulos preservam suas escalas responsivas.

Tokens semânticos foram consolidados no `:root` de `para.css`, e os tokens Pará de texto passam a usá-los. Contrastes calculados dos pares opacos de referência:

| Uso | Texto ou indicador | Fundo | Razão |
| --- | --- | --- | ---: |
| Principal | #203c32 | #fffdf8 | 11,78:1 |
| Secundário | #526359 | #fffdf8 | 6,28:1 |
| Inverso | #fff6df | #143f32 | 10,91:1 |
| Erro | #a63725 | #fffdf8 | 6,47:1 |
| Borda de campo | #76877b | #ffffff | 3,80:1 |
| Foco em superfície clara | #215f87 | #fffdf8 | 6,77:1 |

Dois defeitos renderizados foram confirmados pelo axe e corrigidos: título da Cuia na home com #ffe2a0 sobre #fffaf2 (1,21:1) e parágrafo de leitura da ficha com #526359 sobre #0d281e (2,45:1). A home agora usa texto escuro no card claro; o painel escuro da ficha usa texto inverso. A cor de aviso #c68e2d foi substituída por #79520b e textos/badges cinza usam #526359. Gradientes deixam de interferir no modo de alto contraste.

O aumento revelou causas de overflow corrigidas no cabeçalho público, cabeçalho administrativo, painel Cuia Acessível, rodapé, filtros da comunidade, cards de localidades, formulário de exploração e formulários da conta. Grids usam trilhas flexíveis, controles podem quebrar linha e textos longos não forçam a largura da página. Cabeçalhos pequenos deixam de cobrir conteúdo durante a rolagem. As tabelas mantêm a rolagem dentro dos próprios contêineres.

## Testes e evidências

- Build Release da solução: 0 erros, 0 avisos.
- Unitários: 293 aprovados.
- Integração final: 168 aprovados, 1 ignorado. O cenário ignorado exige contas persistentes de homologação.
- Ensaio adicional optativo: 2 testes de mapa aprovados, incluindo os quatro tamanhos de catálogo e limpeza dos dados sintéticos.
- Teste do repositório cobre 505 publicados, cursor sem duplicações, filtro que encontra registro além do antigo corte, SQL malicioso, área vazia, coordenada inválida, pendente e cancelamento.
- Playwright público: 14 rotas × 6 larguras (320, 360, 390, 768, 1024 e 1440), sem erros JavaScript, overflow ou texto essencial abaixo de 16 px nas seleções verificadas. Axe WCAG A/AA, 2.1 AA e 2.2 AA executado a 320 px, sem violações apontadas.
- Ampliação de texto público: 14 rotas × 3 larguras (320, 768 e 1440) a 200%, sem falhas apontadas.
- Áreas privadas: 14 capturas × 6 larguras em tamanho padrão e 14 capturas a 320 px com texto a 200%, sem falhas apontadas.
- Capturas autenticadas geradas por testes de integração: conta, favoritos, parceiro, cobertura, crawler, discovery, discovery360, editorial, mobilidade, moderação, operação, receitas, sugestões e usuários. São documentos renderizados com identidade de teste; a navegação visual usa interceptação local de HTML, sem contornar autenticação de produção.
- Interações: nenhum Leaflet/API antes do clique, agrupamento, deduplicação, zoom, API indisponível, recuperação, bbox inválido, persistência das cinco escalas, Escape, foco, alto contraste e captura forced-colors. Nenhuma violação axe no mapa aberto e no alto contraste testado.
- Cuia Rotas: cinco interações aprovadas para geolocalização sob ação, salvar sem origem, remover plano, compartilhar sem origem e carregar mapa após clique.
- Script de leitura: persistência, recuperação de armazenamento inválido/indisponível, escalas e migração de preferência aprovadas.

Evidências locais em `artifacts/sprint44/` (diretório ignorado pelo Git):

- `before/metrics.json` e `before/mapa-390.png`;
- `after-final/metrics.json`, `after-final/mapa-390.png` e `after-slow/metrics.json`;
- `scale.json`, com tempos e EXPLAIN;
- `audit-final/report.json` e capturas públicas de 320/1440 px;
- `audit-private/report.json`, capturas privadas de referência e `snapshots/`;
- `text-200-final/report.json`, `private-text-200-final/report.json` e capturas;
- `interactions/report.json`, mapa aberto, cinco escalas, alto contraste e forced-colors;
- `build.log`, `unit.log`, `integration-final.log`, `map-tests.log` e `visit-interactions.json`.

Os snapshots contêm tokens e identidades de teste; não devem ser publicados como artefatos públicos sem higienização.

### Reprodução

```powershell
dotnet build BelemRestaurantes.sln -c Release
dotnet test tests/BelemRestaurantes.UnitTests -c Release
$env:BELRESTAURANTES_VIEW_SNAPSHOTS="$PWD/artifacts/sprint44/snapshots"
dotnet test tests/BelemRestaurantes.IntegrationTests -c Release
# Somente banco de desenvolvimento local:
$env:MAP_SCALE_TEST='1'
$env:MAP_SCALE_OUTPUT="$PWD/artifacts/sprint44"
dotnet test tests/BelemRestaurantes.IntegrationTests -c Release --filter FullyQualifiedName~Map
Remove-Item Env:MAP_SCALE_TEST
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/BelemRestaurantes.Web -c Release --no-build --no-launch-profile --urls http://localhost:5180
# Em outro terminal; dependências: artifacts/ui-test/node_modules/{playwright,@axe-core/playwright}
node scripts/measure-map.cjs
node scripts/verify-sprint44.cjs
node scripts/verify-map-interactions.cjs
node scripts/test-reading-tools.cjs
```

`MAP_OUTPUT` escolhe o diretório de evidências; `SLOW=1` ativa a rede simulada no coletor. `AUDIT_TEXT_SIZE=200` ativa a ampliação, `AUDIT_WIDTHS` e `AUDIT_ROUTES` selecionam casos. `AUDIT_SNAPSHOT_DIR` fornece as capturas autenticadas. A auditoria aguarda uma janela de rate limiting quando recebe 429, preservando a proteção real.

## Pendências para homologação integral

1. Contratar/configurar base de ruas e gateway, validar atribuição e capacidade. O mapa atual oferece posições interativas e lista, com aviso explícito de ausência de ruas.
2. Medir Google Routes real com credencial e orçamento de homologação, e o iframe na rede do público. A integração paga não foi acionada neste trabalho. Homologar itinerário sobre mapa de trajeto, respeitando termos do provedor; hoje o mapa interno é do destino.
3. Medir INP com interações representativas e percentis em homologação. Repetir ensaios sem cargas concorrentes e medir primeiro mapa de ruas útil com o provedor ativo.
4. Homologar MariaDB e planos SQL com distribuição geográfica/estatísticas de produção. O ensaio local tem dados concentrados, não todas as distribuições possíveis de 50 mil lugares.
5. Concluir avaliação manual com leitor de tela, zoom nativo de navegador a 200%, estados hover/active/disabled/error e casos sobre imagens/transparências. Axe, reflow, ampliação de texto e captura de alto contraste não equivalem a certificação integral WCAG 2.2 AA.
6. Revalidar dashboards com dados reais volumosos e modais de cada fluxo; a auditoria privada usa estados renderizados pelos testes. Não há evidência para declarar todas as jornadas de administração/monetização homologadas em todos os seus estados.

Os commits locais registram código, testes e este relatório. Backend: commit `8808b6c`. A interface, os scripts de auditoria e este relatório estão no commit seguinte. Os arquivos alterados podem ser consultados com `git show --stat` e `git show --name-only` no commit da sprint. Não houve publicação, push ou alteração de configuração de produção.
