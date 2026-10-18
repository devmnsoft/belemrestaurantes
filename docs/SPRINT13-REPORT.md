# Sprint 13 — descoberta real, catálogo e participação

Validação local em 18/10/2026, America/Sao_Paulo.

**Resultado:** 120 estabelecimentos reais de Belém descobertos no OpenStreetMap e persistidos no MySQL como `pending`. Web e Worker executados separadamente em Development. A publicação dos estabelecimentos depende da moderação humana existente; o catálogo público pode continuar vazio até essa revisão.

## Robô e evidência real

- A chave disponível de Google Places continua incompatível com Places API (New). Foi adicionada uma fonte alternativa explícita: `Discovery:Provider=OpenStreetMap`, habilitada no Worker Development. Google Places permanece disponível para ambientes com chave válida.
- A consulta Overpass seleciona a fronteira administrativa do município oficial do Pará e até 120 estabelecimentos nomeados, com coordenadas. Faz mapeamento de endereço, categoria, telefone, site e redes presentes na fonte. Dados ausentes não são inventados.
- O snapshot real de Belém foi coletado em **18/10/2026 às 07:37:09 UTC**. Foram confirmados **120 registros distintos no catálogo, todos pendentes**. Fixtures de teste são removidas e não fazem parte desse total.
- O Worker executa a fila a cada minuto, lote 2; o Crawler continua habilitado com até 3 tarefas por ciclo. A coleta externa é reutilizada durante 24 horas por município, entre categorias e reinícios. Um ciclo por minuto não significa uma chamada externa nova por minuto.
- Foram recuperadas 8 tarefas em dead letter de Belém. O painel Discovery permite repetir falhas de uma cidade, com autenticação, autorização e antiforgery, sem alterar tarefas em execução.
- A telemetria registra a fonte efetiva. Contagens por ciclo incluem reencontros/atualizações e não devem ser somadas para obter estabelecimentos distintos. A data da observação preserva a coleta original do snapshot.
- Falhas temporárias HTTP 502 foram observadas antes da coleta bem-sucedida. Respostas parciais, sem coordenadas ou inválidas não são aceitas como sucesso completo. IDs de OSM, crawler e sugestões não são enviados ao endpoint Google Details.
- Corrigidos timestamps UTC de ingestão e duplicação de linhas na listagem de moderação.

A fonte exige atribuição e observância da [licença OpenStreetMap/ODbL](https://www.openstreetmap.org/copyright). O rodapé identifica os colaboradores, e `/dados-abertos/openstreetmap` disponibiliza os snapshots da fonte, sem dados privados de usuários ou parceiros. O endpoint público Overpass é usado com cache e volume limitado em Development; produção deve configurar instância própria por `OpenData:Endpoint`, conforme a [política de uso do servidor](https://dev.overpass-api.de/overpass-doc/en/preface/commons.html). A consulta usa a [linguagem oficial Overpass QL](https://wiki.openstreetmap.org/wiki/Overpass_API/Overpass_QL).

## Design e funcionalidades

- Busca reconstruída com abertura editorial, filtros laterais, recolhimento mobile, grade/lista, ordenação por nome, cidade, recentes e proximidade; filtro por bairro e foto, chips e paginação preservando parâmetros.
- Botão “perto de mim” solicita geolocalização somente ao clicar. Coordenadas e raio são validados; distância é calculada no banco com SQL parametrizado. Permissão negada recebe feedback.
- Página do estabelecimento com hierarquia renovada, fotos ou placeholder ilustrativo explícito, telefone, mapa por coordenadas, redes, fonte, compartilhar, favorito e sugestão de correção. Corrigida autenticação do membro em página pública e materialização Dapper de comentários/galeria, que causava erro 500.
- Nova página `/sugerir`, com indicação ou correção de estabelecimento, municípios oficiais, validação HTTPS, antiforgery, honeypot e limite de 5 envios/hora. Não envia mensagens externas.
- Novo painel `/admin/suggestions`: aceitar indicação cria estabelecimento pendente; aceitar correção atualiza o registro publicado original após revisão, sem duplicá-lo; rejeitar encerra a sugestão. Mutações possuem autorização e auditoria.
- Home ganhou atalhos para explorar, indicar e favoritos; rodapé ganhou fonte e exportação. Roteiros sem registros agora exibem um estado vazio útil.
- Arte de Belém mantida como ilustração e convertida para WebP: **3.505.799 para 540.764 bytes**, aproximadamente 85% menor. Home e telas de acesso usam o arquivo otimizado; original preservado.
- Cores e componentes seguem a identidade Pará implantada na sprint anterior. Removidas afirmações sem comprovação sobre auditoria e integrações de mobilidade.

## Banco e implantação

- **018_open_data_cache.sql:** snapshot persistente por município.
- **019_suggestion_corrections.sql:** vínculo opcional de sugestão com estabelecimento alvo, com chave estrangeira.
- Ambas aplicadas no banco local. Migrations anteriores preservadas; teste de histórico/checksums cobre 19 scripts.
- Artefatos Release preparados em `artifacts/sprint13/publish/Web` e `artifacts/sprint13/publish/Worker`. Não houve implantação na VPS/IIS nem instalação de Windows Service nesta sessão.

## Verificação

- Build Release da solução: **zero erros e avisos**.
- **167 testes unitários aprovados**.
- **53 testes de integração selecionados aprovados**: migrations, municípios, endpoints, segurança, ingestão/moderação/publicação controladas, favoritos, proximidade, filtros, indicação/correção administrativa, exportação e telas de operação.
- Testes de integração usam registros controlados e os removem. A jornada de publicação validada por testes não representa publicação dos 120 estabelecimentos reais.
- Revisão responsiva: 105 combinações de tela/largura em 360, 390, 768, 1024 e 1440 px. Inclui páginas públicas, snapshots autenticados do host de integração e páginas preenchidas com fixtures. A última revisão repete somente as telas alteradas, preservando os resultados das demais.
- Resultado responsivo: **105 casos aprovados, sem overflow, erros JavaScript ou violações nas regras axe selecionadas**. Axe executado nas regras WCAG A/AA selecionadas em 360 px. A verificação automatizada não equivale a certificação integral de acessibilidade ou pentest.
- Testes de concorrência que consomem a fila operacional foram excluídos desta seleção para preservar os jobs reais.

Evidências locais, ignoradas pelo Git:

- `artifacts/sprint13/discovery-evidence.json`: consulta somente leitura ao catálogo e aos ciclos reais.
- `artifacts/sprint13/worker.log` e `worker-final.log`: primeira coleta e ciclos/cache finais.
- `artifacts/sprint13/open-data-export.json` e `fonte-openstreetmap.csv`: snapshot e 120 itens da fonte para revisão, sem equivalência a aprovação editorial.
- `artifacts/sprint13/unit.trx`, `integration.trx`, `integration.log`.
- `artifacts/sprint13/visual/report.json`, `responsive-summary.json` e screenshots.
- `artifacts/test-results/admin-snapshots/*.html`: páginas de teste autenticadas/controladas, sem bypass no ambiente real.

## Pendências externas

1. Moderar os 120 estabelecimentos, conferir nomes, contatos e dados desatualizados antes da publicação. Fotos reais dependem de origem autorizada.
2. Configurar chave válida de Places API (New) somente se optar pelo provedor Google. A alternativa aberta já funciona em Development. DeepSeek permanece opcional e sem chave configurada.
3. Rotacionar credenciais expostas historicamente no Git nos respectivos provedores. Retirar do código não invalida segredos antigos.
4. Implantar os artefatos e migrations na VPS; configurar serviço, segredos e fonte de produção. Para OSM contínuo, usar endpoint Overpass próprio e manter atribuição/exportação.

## Arquivos envolvidos

O manifesto abaixo enumera os arquivos alterados/adicionados nesta entrega. Artefatos ignorados não entram no código versionado.

- `.env.example`
- `docs/SPRINT13-REPORT.md`
- `README.md`
- `scripts/verify-responsive.cjs`
- `src/BelemRestaurantes.Application/Abstractions/IExperienceService.cs`
- `src/BelemRestaurantes.Application/Abstractions/IOpenDataCache.cs`
- `src/BelemRestaurantes.Application/Abstractions/IRepositories.cs`
- `src/BelemRestaurantes.Application/Abstractions/ISuggestionRepository.cs`
- `src/BelemRestaurantes.Application/Models/DiscoveryModels.cs`
- `src/BelemRestaurantes.Application/Models/RestaurantView.cs`
- `src/BelemRestaurantes.Application/Services/DiscoveryOrchestrator.cs`
- `src/BelemRestaurantes.Infrastructure/Google/GooglePlacesClient.cs`
- `src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/018_open_data_cache.sql`
- `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/019_suggestion_corrections.sql`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryQueue.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryTargetRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlModerationRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlOpenDataCache.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlRestaurantIngestionRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlRestaurantReadRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlSuggestionRepository.cs`
- `src/BelemRestaurantes.Infrastructure/OpenData/OpenStreetMapDiscoveryClient.cs`
- `src/BelemRestaurantes.Web/Controllers/AccountController.cs`
- `src/BelemRestaurantes.Web/Controllers/AdminOperationsController.cs`
- `src/BelemRestaurantes.Web/Controllers/AdminSuggestionsController.cs`
- `src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs`
- `src/BelemRestaurantes.Web/Controllers/OpenDataController.cs`
- `src/BelemRestaurantes.Web/Controllers/SearchController.cs`
- `src/BelemRestaurantes.Web/Controllers/SuggestionsController.cs`
- `src/BelemRestaurantes.Web/Models/RestaurantSuggestionInput.cs`
- `src/BelemRestaurantes.Web/Program.cs`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Discovery.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminSuggestions/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml`
- `src/BelemRestaurantes.Web/Views/Home/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Search/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_AuthStory.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_RestaurantCard.cshtml`
- `src/BelemRestaurantes.Web/Views/Suggestions/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Trails/Index.cshtml`
- `src/BelemRestaurantes.Web/wwwroot/css/para-discovery.css`
- `src/BelemRestaurantes.Web/wwwroot/css/para.css`
- `src/BelemRestaurantes.Web/wwwroot/images/belem-sabores-editorial.webp`
- `src/BelemRestaurantes.Web/wwwroot/js/catalog.js`
- `src/BelemRestaurantes.Worker/appsettings.Development.json`
- `src/BelemRestaurantes.Worker/ConfigurationDiagnosticsService.cs`
- `src/BelemRestaurantes.Worker/Program.cs`
- `tests/BelemRestaurantes.IntegrationTests/CatalogJourneyTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/DiscoveryOperationsTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/NearbyCatalogTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/PublicDiscoveryJourneyTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/SecurityEndpointTests.cs`
- `tests/BelemRestaurantes.UnitTests/CuiaExperienceV5Tests.cs`
- `tests/BelemRestaurantes.UnitTests/OpenDataDiscoveryTests.cs`
