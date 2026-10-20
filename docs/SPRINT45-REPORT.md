# Sprint 45 Legibilidade rotas e catálogo público

Validação local em 20/10/2026. Foram corrigidas falhas de contraste, erros 500 de cardápio e Portal Parceiro e ações que dependiam de JavaScript bloqueado pela CSP. As fichas publicadas passam a aparecer na home e na descoberta da Cuia, separadas das recomendações verificadas. O fluxo de geometria de rotas foi implementado e exercitado com respostas simuladas.

**A sprint permanece parcialmente homologada:** não há credenciais de transporte configuradas nem cinco viagens reais comprovadas, e não houve publicação de cadastros pendentes sem evidência suficiente. A quantidade publicada continua em 57. Não houve push, publicação do site ou mudança de configuração de produção.

## Diagnóstico e correções

| Jornada | Causa comprovada | Correção | Evidência |
| --- | --- | --- | --- |
| Home vazia apesar dos publicados | Busca limitada a seis resultados Guia; o banco local tem zero Guia | Seção de descoberta de fichas, com contagem e acesso ao catálogo paginado | HTML real, teste de integração e capturas |
| Cuia sem lugares para explorar | Recomendações consultavam exclusivamente Guia | `DirectoryMatches` separado, sem alimentar recomendações verificadas ou contexto do provedor generativo | Resposta do serviço, SSR e resultado dinâmico |
| Traçado de ônibus ausente | Geometria não era solicitada, decodificada ou desenhada | Geometria da rota e etapas, validação, mapa Google progressivo e seleção de trechos | Testes de parser e renderizador simulado |
| Texto branco sobre fundo branco | `.card` aparecia depois de `.bg-forest` e sobrescrevia seu fundo | Variante `.card.bg-forest` no componente de cards, com texto e secundários próprios | Medição de estilos computados no cardápio |
| Alerta de fechamento ilegível | #e65100 sobre #fff3e0 a 18 px | #8f3500 sobre a mesma superfície | Antes 3,45:1; depois 7,15:1 |
| Cardápio e páginas do parceiro retornando 500 | Oito Views convertiam `RestaurantView` em entidade `Restaurant` | Uso do modelo de leitura efetivamente retornado pelos controllers | Oito páginas autenticadas/públicas testadas |
| Estatísticas do parceiro retornando 500 | Dapper não tratava parâmetro `DateOnly` | Handler MySQL de data, registrado junto aos handlers existentes | Consulta com estatística gerada no teste |
| QR Code e sugestão de descrição sem ação | Dependência de Bootstrap JS ausente e script inline bloqueado por CSP | Dialog nativo, cópia declarativa e scripts externos com tratamento de erro | Playwright, Escape, foco e chamadas simuladas |
| Formulários sem nomes acessíveis | Campos de ordenação, redes sociais, evento e horários sem associação | Nomes acessíveis específicos, inclusive por dia da semana | Axe nas páginas do proprietário |
| Horários sugeridos como se já existissem | Segunda marcada fechada e horários 11:30–23:00/12:00–18:00 pré-preenchidos | Ausência deixa campos vazios e não presume dia fechado | HTML do parceiro e revisão da View |
| Estilos antigos após atualização | Imports CSS tinham URLs estáveis independentes do hash de `para.css` | Hash de conteúdo em cada import local | `scripts/version-style-imports.py` |

Também foram substituídos tokens de alerta sem definição em estados de Crawler, Discovery360 e avisos administrativos. As variantes escuras da Cuia foram retiradas de estilos inline conflitantes. O painel de moderação agora mostra a data da última observação da fonte, avisando que coleta não confirma funcionamento atual.

### Contraste renderizado

O ensaio de superfícies usa a página de cardápio atual com CSS anterior interceptado apenas no navegador de teste, e depois com o CSS corrigido. Isso isola o defeito de estilo; não representa uma versão anterior funcional do cardápio, que retornava 500.

O card antigo teve texto e fundo computados como `rgb(255,255,255)`: **1:1**. Depois, o fundo é `rgb(20,63,50)` e o texto principal `rgb(255,246,223)`, aproximadamente **10,91:1**. O axe não sinalizou o caso branco sobre branco nesse ensaio, reforçando a necessidade de inspeção visual e medição de estilos. A inspeção final também corrigiu o botão de QR Code que herdava texto verde sobre botão verde; ele agora usa dourado com texto verde. Estados vazios deixaram de afirmar que há atualização de cardápio em andamento sem informação do estabelecimento.

O alerta de fechamento foi reproduzido com a folha anterior em uma galeria identificada como fixture. Não foi criado estabelecimento fictício fechado no catálogo público. Os relatórios guardam seletor, estado, cores e falha correspondente. Estados de menu aberto, hover, erro, foco, disabled, input inválido, ampliação e alto contraste também têm capturas.

## Transporte e desenho do itinerário

`GoogleRoutesProvider` solicita geometria geral e por etapa, preservando sentido, paradas, localização das paradas e duração informada. Destinos e origens geográficas válidas usam `latLng`, evitando tratá-los como endereços para geocodificação. O parser valida números finitos, limita a geometria e rejeita uma sequência malformada inteira.

`RouteGeometry.Decode` implementa o formato encoded polyline com precisão 5. Não gera linha reta de substituição. Dados ausentes continuam ausentes. O limite existente de caminhada continua sendo aplicado. A quantidade de baldeações é derivada dos trechos de transporte recebidos; tarifas só aparecem quando fornecidas.

Cada alternativa tem itinerário textual, botão de mapa e dados de geometria no HTML privado da resposta POST. O mapa só carrega após interação. Caminhada usa traço descontínuo; transporte usa traço contínuo. Paradas só recebem marcador quando há coordenada da fonte. Selecionar uma etapa ajusta o enquadramento; selecionar o traçado pode focar a etapa textual.

O mapa de trajetos Google usa Google Maps JavaScript, conforme a [política de exibição do Routes API](https://developers.google.com/maps/documentation/routes/policies). Não há desenho de resultados Google sobre Leaflet/OSM. O mapa geral do catálogo permanece independente. A geometria recebida não é salva em cache persistente ou no plano compartilhado.

O loader tem prazo de 12 segundos, recuperação de falha de rede/autorização e preserva o texto. A chamada Routes mantém seu prazo de 8 segundos, orçamento diário e cancelamento. O POST de consulta recebeu limite específico de dez consultas por minuto/IP. Erros de configuração, limite do provedor e indisponibilidade são estados distintos. A CSP só permite os recursos Google necessários nas páginas de rotas quando a integração de mapa está habilitada.

Referências técnicas: [geometria e campos de computeRoutes](https://developers.google.com/maps/documentation/routes/reference/rest/v2/TopLevel/computeRoutes), [encoded polyline](https://developers.google.com/maps/documentation/utilities/polylinealgorithm) e [carregamento da API JavaScript](https://developers.google.com/maps/documentation/javascript/load-maps-js-api).

### Dependências para ativar

O levantamento de configuração encontrou Routes desabilitado, sem chave, sem orçamento diário e sem chave de navegador. O usuário não soube informar URL de homologação/produção ou provedor contratado. Nenhuma credencial foi exposta ou criada e nenhuma consulta paga foi feita.

Configurar nos segredos do ambiente:

- `GoogleRoutes:Enabled=true`, `GoogleRoutes:ApiKey` e `GoogleRoutes:DailyRequestLimit`, com limite aprovado para a conta;
- `GoogleMaps:Enabled=true` e **chave separada** `GoogleMaps:BrowserKey`, restrita aos domínios autorizados e à Maps JavaScript API;
- APIs, faturamento, restrições da chave de servidor e termos/privacidade compatíveis com o contrato.

A chave de servidor não é enviada ao navegador. A chave específica de navegador é pública por finalidade e exige restrições de origem/API. Não há substituição automática entre as duas chaves.

### Cobertura e cinco viagens reais

Foram localizadas referências do operador sobre [linhas do BRT Metropolitano](https://ajuda.prajametropolitano.com.br/hc/pt-br/articles/44079101760795-Conhe%C3%A7a-as-linhas-do-BRT-Metropolitano), a [listagem oficial do aplicativo Vem Busão](https://play.google.com/store/apps/details?id=br.com.m2m.meuonibus.belem) e informações institucionais de transporte. Essas páginas não comprovam autorização de uso de uma API ou feed GTFS para integração no portal. Nenhum feed com cobertura, licença e validade comprovadas foi integrado neste trabalho.

`artifacts/sprint45/transit-live-pending.json` registra cinco pares propostos: origens públicas em Belém e destinos reais já publicados no banco. Todos estão marcados **not_executed**, com zero chamadas pagas e motivo do bloqueio. Não existem linhas, horários, tarifas ou durações fictícias nesse arquivo. A cobertura real precisa ser demonstrada quando o provedor for habilitado.

## Catálogo e procedência

Auditorias somente de leitura, antes e depois, no banco local:

| Indicador | Antes | Depois |
| --- | ---: | ---: |
| Cadastros | 25.229 | 25.229 |
| Publicados | 57 | 57 |
| Pendentes | 25.172 | 25.172 |
| Rejeitados / suspensos | 0 / 0 | 0 / 0 |
| Ficha publicada / Guia | 57 / 0 | 57 / 0 |
| Publicados com coordenada válida | 57 | 57 |
| Cadastros com endereço | 17.615 | 17.615 |
| Cadastros com telefone | 17.129 | 17.129 |
| Foto licenciada indicada na coluna do catálogo | 0 | 0 |
| Cadastros com horário regular | 0 | 0 |

Há registros em 141 municípios e 27 categorias. Entre as lacunas prioritárias estão Parauapebas (1.413 cadastros), Santarém (1.400) e Ananindeua (1.206), todos sem publicados no recorte local auditado. Os publicados locais estão em Belém. Distribuições completas por município, categoria e estágio estão nos JSONs. Há 187 grupos candidatos a duplicidade, abrangendo 413 registros; isso não comprova que todos devam ser mesclados. Observações por fonte incluem 24.272 registros associados a Minha Receita e 964 a OpenStreetMap, podendo existir mais de uma fonte para o mesmo cadastro.

O funil persistido permite contar pendentes e publicados; não há uma etapa independente que comprove visita/validação de todos os descobertos. O gargalo atual é a revisão de 25.172 pendentes, a falta de enriquecimento e a visibilidade restrita ao Guia na home. Acrescentar mais coleta sem resolver isso não aumentaria automaticamente a oferta pública útil.

A home agora apresenta até 12 fichas e informa a quantidade total dessa camada, com acesso à busca paginada de todas as fichas. Guia permanece separado. Cards oferecem acesso direto ao transporte. A Cuia mantém recomendações restritas ao Guia; quando não as encontra, pode mostrar até quatro fichas correspondentes em uma seção de exploração, com advertência sobre confirmação de funcionamento e acessibilidade. Isso vale para SSR e resultados assíncronos.

Uma seleção de candidatos foi obtida pelos critérios já existentes de moderação: município oficial, nome, categoria, endereço, fonte HTTPS e ausência de alerta de duplicidade. Cinco elementos OSM foram consultados diretamente e registrados em `osm-reviewed.json`; suas datas de última edição variam de 2017 a 2024. Consulta atual do registro OSM não comprova funcionamento atual do negócio. Resultados com nomes semelhantes também não bastam para confirmar identidade/endereço.

**Transições de publicação neste trabalho: zero.** Os candidatos permaneceram pendentes. Não foram criados restaurantes sintéticos no catálogo público nem promovidas fichas a Guia para preencher a home. Os dados sintéticos dos testes de integração são removidos em `finally`.

## Fluxos exercitados

- Busca → ficha → cardápio/Como chegar: navegação pública e cardápio sem erro de modelo.
- Cuia → ficha de descoberta → transporte: seção separada e links seguros, sem classificar ficha como recomendação verificada.
- Município/bairro → busca/mapa: filtros e paginação existentes preservados; nenhuma publicação pendente foi liberada.
- Roteiros → parada → transporte: links existentes preservados; a variante de card escuro foi corrigida. Não foram inventadas paradas para preencher roteiros vazios.
- Parceiro → perfil/categoria → consulta pública: teste cria categoria e grava perfil com antiforgery, consulta as oito páginas e limpa os dados.
- Parceiro → sugestão de descrição: script externo permitido pela CSP, exige nome, limita espera, permite recuperar de erro e pede revisão antes de salvar. O teste da resposta de IA é simulado.
- Cardápio → QR Code: dialog nativo, Escape e restauração de foco, nome acessível para o link e imagem externa solicitada somente após clique.

## Testes e artefatos

Build Release final: zero erros e avisos. Unitários: **304 aprovados**. Integração completa: **171 aprovados, um ignorado**, seguido de teste direcionado atualizado do parceiro. O caso ignorado depende de contas persistentes de homologação. O teste de concorrência da fila recebeu prioridade exclusiva para seus próprios alvos, evitando assumir que não existam outros jobs devidos no banco compartilhado.

Playwright/axe e capturas locais:

- 14 rotas públicas × 6 larguras: 320, 360, 390, 768, 1024 e 1440;
- 14 rotas públicas × 3 larguras com texto a 200%;
- 14 documentos autenticados × 6 larguras e os mesmos 14 a 320 px com texto a 200%;
- oito páginas adicionais de cardápio/parceiro × seis larguras e novamente a 320 px com texto a 200%, sem falhas apontadas após corrigir nomes de controles e reflow;
- oito estados dinâmicos de componentes, incluindo hover/focus de recuperação, erro e alto contraste;
- geometria, seleção de etapa, marcadores e recuperação de falha simulados, sem chamadas pagas;
- dialog e sugestão de cardápio, com sucesso/erro e revisão antes de salvar.

As capturas autenticadas são HTML gerado por testes com identidade de fixture e servido por interceptação local do navegador; não equivalem a uma sessão de produção. Fixtures de transporte estão identificadas como **TESTE SIMULADO**. O ensaio de rede da Sprint 45 registrou cinco carregamentos de `/mapa`, mediana TTFB 27 ms, LCP 536 ms e CLS 0, sob carga concorrente local. Não há comparação equivalente antes/depois nesta sessão para afirmar ganho de desempenho. INP não foi amostrado.

Diretório de evidências: `artifacts/sprint45/`, ignorado pelo Git. Inclui `catalog-before.json`, `catalog-after.json`, `osm-reviewed.json`, `transit-live-pending.json`, relatórios/capturas em `public`, `text200`, `private`, `private-text200`, `partner-pages-final`, `partner-pages-text200`, `states`, `states-before`, `dark-surfaces`, `menu`, `transit`, e logs de build/testes. Há dados e tokens de teste nos snapshots; não publicar o diretório sem higienização.

### Reprodução

```powershell
python scripts/version-style-imports.py
dotnet build BelemRestaurantes.sln -c Release
dotnet test tests/BelemRestaurantes.UnitTests -c Release
$env:BELRESTAURANTES_VIEW_SNAPSHOTS="$PWD/artifacts/sprint45/snapshots"
dotnet test tests/BelemRestaurantes.IntegrationTests -c Release
$env:CATALOG_AUDIT_OUTPUT="$PWD/artifacts/sprint45/catalog-after.json"
dotnet test tests/BelemRestaurantes.IntegrationTests -c Release --filter FullyQualifiedName~CatalogAuditTests
# Servidor local Development iniciado separadamente, após a compilação:
node scripts/verify-sprint45-states.cjs
node scripts/verify-transit-renderer.cjs
node scripts/verify-menu-dialog.cjs
node scripts/verify-menu-suggestion.cjs
```

As ferramentas de navegador usam as dependências Playwright/axe em `artifacts/ui-test/node_modules`. `verify-sprint44.cjs` continua sendo o executor da matriz de rotas; `MAP_OUTPUT`, `AUDIT_ROUTES`, `AUDIT_WIDTHS`, `AUDIT_TEXT_SIZE` e `AUDIT_SNAPSHOT_DIR` selecionam os casos. O ensaio de CSS anterior exige os arquivos extraídos do commit anterior, como descrito nos próprios scripts.

## Pendências de homologação

1. Configurar o provedor e comprovar os cinco pares reais de ônibus, com fonte, duração, etapas e traçado da mesma resposta. Hoje o renderizador foi validado em simulação.
2. Verificar operação atual e resolver duplicidades dos candidatos antes de ampliar o número publicado. As 57 fichas agora estão mais acessíveis, mas não houve crescimento quantitativo validado.
3. Validar em produção/homologação a configuração de publicação, cache, migrações e restrições de chave. Nenhuma URL autorizada foi informada.
4. Concluir teste manual com leitor de tela, zoom nativo de navegador a 200% e inspeção completa de todos os estados de dados reais. Ampliação de texto, reflow, screenshots e axe não equivalem a certificação integral WCAG.
5. Homologar a disponibilidade e atribuição visual do mapa Google real e suas próprias interfaces de controle. O mock testa o contrato do renderizador, não a rede ou cobertura do Google.

Backend e contratos: commit `388f954`. A interface, os scripts e este relatório estão no commit seguinte. Os commits locais registram código e testes; `git show --stat` e `git show --name-only` fornecem a lista de arquivos de cada entrega. A sprint não deve ser declarada integralmente concluída antes das pendências acima.
