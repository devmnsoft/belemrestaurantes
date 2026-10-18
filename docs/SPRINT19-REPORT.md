# Sprint 19 — operação, receitas e descoberta estadual

Projeto Belém Restaurantes · MNSOFT · execução em 18/10/2026.

## Resultado e limites de publicação

O portal local recebeu o módulo **Receitas do Pará**, evolução das páginas de estabelecimentos, navegação administrativa adaptada ao celular, aprovação e publicação separadas, planejamento de descoberta nos 144 municípios e rotinas persistentes de verificação da validade de horários e fontes de mobilidade.

**Publicação comprovada no banco e na aplicação local; não houve implantação no IIS ou instalação de serviço Windows nesta sprint.** Os pacotes Release estão em `artifacts/sprint19/publish/web` e `artifacts/sprint19/publish/worker`. O Worker continua independente da Web.

## Catálogo real e aprovação

O retrato de 18/10 às 11h16 de Belém registrou **17.888 cadastros em 141 municípios: 17.886 pendentes e dois publicados**. A execução é contínua; `artifacts/sprint19/operations.json` contém o retrato mais recente. Cadastros de empresas não equivalem a lojas abertas e verificadas. A meta de 100 mil não foi alcançada nem apresentada como quantidade existente.

Dois lugares descobertos anteriormente pelo OpenStreetMap passaram por conferência de fonte e pelo mesmo serviço de moderação usado pelo painel:

| Cadastro | Endereço conferido | Conferência adicional |
| --- | --- | --- |
| Amazon Beer, ID 174 | Armazém/galpão 1 da Estação das Docas, Belém | [Marca na distribuidora Gabi](https://distribuidora.gabi.com.br/all-service-list/amazon-beer/) e [administração do complexo](https://estacaodasdocas.com/estacao-das-docas-completa-21-anos-nesta-quinta-feira-13-e-se-mantem-como-referencia-em-complexos-turisticos/) |
| AC GastroPub, ID 428 | Avenida Alcindo Cacela, 3327, Cremação, Belém | [Página comercial do estabelecimento](https://www.lojalocal.com/44608567000110) |

Não foram copiadas fotografias, avaliações, preços ou receitas desses locais. As observações OSM e suas URLs permanecem no banco. Não se afirmou funcionamento em tempo real.

Lote de aprovação: `30d759c8-8837-460b-9abd-02c4d475a67a`. Lote de publicação: `4553890a-ec3e-48b8-99f6-1a7aaaad11ec`. Ambos aplicaram os dois itens, sem falhas. Evidência: `artifacts/sprint19/real-publication.json`; auditoria persistida com ator explícito `local:authorized-curation`, sem impersonar usuários.

Páginas locais: `/estabelecimento/174`, `/estabelecimento/428`, `/busca`. Roxy e Point do Açaí apresentaram pendências de endereço/possível duplicidade e **não foram liberados automaticamente**. O processo não transforma filiais em um único lugar por coincidência de nome.

## Operação autônoma

- Discovery e crawler em Development: ciclos de um minuto, filas e leases persistentes, retries, telemetria e heartbeat. Proteções de proprietário, SSRF, cotas e moderação permanecem.
- Plano OpenStreetMap configurado para os 144 municípios oficiais. Atualização semanal por alvo; primeira passagem de novos alvos espaçada em dois minutos. Localidades de Belém continuam pertencendo a Belém. Configuração auditada e acionamento também disponível em `/admin/operacao`.
- Ciclo observado às 11h01: 483 lugares encontrados em snapshot válido de Belém; 482 atualizados, um descartado por nome inválido, zero falhas. A utilização de cache é registrada, sem alegar nova requisição externa.
- Coleta cadastral em 21 atividades econômicas retomada com checkpoint. Ciclo real 1185 importou 620 novos lugares, ignorando as 80 páginas já confirmadas; zero falhas. Novos registros permanecem pendentes. Checkpoint e ledger impedem reimportação de páginas confirmadas.
- Jobs `hours` e `mobility` têm fila durável, tentativa, lease, resultado e auditoria. **Verificam validade dos dados existentes; não sincronizam feeds externos.** Resultado informa `validationOnly=true`, `externalRequests=0`. Não há horários ou tarifas inventados.
- Botão de enriquecimento enfileira até 100 lugares com site, preserva dados de proprietário e evita repetição de itens já ativos. A requisição HTTP não aguarda o crawler.
- Quatro URLs iniciais sem feed operacional confirmado foram pausadas pela migração 027, com auditoria e histórico preservado. Tarefas de fontes pausadas ficam pendentes até reativação. Erro de rede, 403, 429 ou 5xx no `robots.txt` adia a coleta; ausência explícita 404/410 permite ritmo conservador.

Evidências: `statewide-plan.log`, `maintenance-queue.log`, `operations.json`, `discovery.json` e logs locais do Worker em `%LOCALAPPDATA%/MNSOFT/BelemRestaurantes/logs`. Os snapshots/checkpoints minimizados da coleta ficam em `artifacts/sprint18/receita-expanded`.

## Aprovação em lote

`/admin/moderacao` oferece filtro por etapa, cidade, categoria, fonte, data, completude, duplicidade e texto; ordenação, paginação, seleção da página ou de todos os resultados filtrados dentro do limite. Aprovar sem publicar, publicar aprovados, aprovação/publicação conjunta, arquivar, bloquear, rejeitar, encaminhar para revisão e reprocessar permanecem operações distintas.

A confirmação congela os IDs por 15 minutos e pertence ao ator. A execução revalida cada cadastro sob bloqueio transacional, registra decisão por item e auditoria, e devolve o mesmo resultado em reenvio. Itens inelegíveis são ignorados com motivo; erros SQL inesperados revertem a transação. Não existe promessa de commit parcial diante de erro de infraestrutura.

A migração 026 adiciona índices às consultas por item de moderação e por observação recente. Corrige o timeout observado na busca de aprovação com o catálogo ampliado; o timeout não foi aumentado.

## Receitas do Pará

Rotas `/receitas`, `/receitas/{slug}` e administração `/admin/receitas`. Foram publicadas **12 receitas originais da redação**, todas identificadas como **adaptações editoriais**, nunca como receitas oficiais de restaurantes:

Tacacá em casa; pato no tucupi caseiro; maniçoba pronta com acompanhamentos; vatapá paraense; arroz paraense com jambu; caldeirada de peixe; peixe na frigideira; açaí com farinha; farofa de castanha-do-pará; tapioca com queijo do Marajó; creme de cupuaçu; suco de cupuaçu.

Busca por título, ingredientes ou restaurante relacionado publicado; filtros por categoria, ocasião, município, restaurante e preparo rápido. Tempo total inclui espera/refrigeração, evitando classificar o creme gelado como receita de dez minutos. Detalhes apresentam ingredientes, etapas, rendimento, tempos, dicas, variações, acompanhamentos, alergênicos, cuidados, referência e revisão. Checklist, modo cozinha com Escape e impressão funcionam sem cadastro ou armazenamento de preferências.

Fontes de contexto incluem [Embrapa sobre tucupi](https://www.embrapa.br/en/embrapa-no-cirio/tucupi), [Embrapa sobre manivas comercializadas](https://www.embrapa.br/en/busca-de-publicacoes/-/publicacao/1132408/composicao-fisico-quimica-e-conformidade-com-a-legislacao-de-manivas-cozidas-comercializadas-em-belem-pa) e [temperaturas de preparo do USDA](https://www.fsis.usda.gov/food-safety/safe-food-handling-and-preparation/food-safety-basics/safe-temperature-chart). As receitas utilizam tucupi comercial pronto, maniçoba já preparada e polpa de açaí pasteurizada. Não ensinam processamento doméstico de maniva crua ou desintoxicação de tucupi cru.

O [vídeo público de tacacá da TV Liberal](https://globoplay.globo.com/v/12465126/) é apenas um link editorial identificado; não foi baixado, incorporado ou reproduzido automaticamente. O CMS aceita links públicos de plataformas permitidas e exige título. Receita oficial exige fonte HTTPS e registro editorial de autorização/publicação. Nenhuma das 12 sementes alega essa autorização.

CMS com papéis de editor, CSRF, rascunho/publicação/arquivamento, histórico, controle de versão contra edição concorrente e associação somente a restaurantes publicados. Reexecutar a semente preserva slugs existentes e alterações editoriais. JSON-LD `Recipe` usa os dados exibidos e escape seguro, sem notas, nutrição ou fotos inventadas. O caderno é em português; não se afirmam traduções de receitas para oito idiomas.

## Mobilidade e design

Cuia Rotas continua oferecendo planejamento por destino, horário, meio de transporte, caminhada, acessibilidade e preferências. Novos filtros de ocasião e duração buscam até três janelas dentro de horários vigentes, respeitando intervalos, reservas, exceções e expiração futura. Não estimam lotação ou filas. Sem dados suficientes, explicam a ausência e oferecem link ao planejador externo.

O mapa de destino nas páginas de estabelecimentos só carrega após clique. Geolocalização depende de ação explícita; planos salvos/compartilhados excluem a origem. Referências fluviais não se tornam linhas ou horários confirmados.

Verde floresta, açaí, tucupi, urucum e tons de rio conectam homepage, busca, estabelecimento, receitas e administração. As receitas usam ilustrações nativas identificadas; estabelecimentos sem fotografia exibem ausência de foto. A página de estabelecimento prioriza nome, endereço, contato e visita. A navegação administrativa recolhe no celular e mantém operação por teclado. Homepage e navegação possuem textos PT/EN/ES; demais culturas mantêm fallback declarado.

As áreas já existentes de guia, sabores, comunidade, experiências, favoritos, roteiros e parceiros permanecem integradas. Não se publicaram depoimentos, selos, cardápios ou roteiros fictícios para preencher essas áreas.

## Contas e segurança

As contas persistentes `superadmin.mnsoft`, `operador.teste` e `editor.teste` foram verificadas com as credenciais protegidas existentes: hash/senha corretos e desafio de MFA. Os papéis permanecem separados. O fluxo completo de cookie, MFA, troca inicial e limites de autorização é exercitado com contas temporárias de teste, removidas ao final. **A configuração pessoal do MFA e a troca inicial das três contas reais continuam necessárias para uso interativo.** Não foram burladas para gerar evidências.

As senhas não constam deste relatório, do chat ou do Git. Estão protegidas por DPAPI para o usuário Windows atual em `%LOCALAPPDATA%/MNSOFT/BelemRestaurantes/homologacao.credentials.dpapi`. Para visualização pelo próprio operador: `scripts/windows/show-homologation-credentials.ps1`. Não executar esse comando em logs compartilhados.

Helpers locais de curadoria exigem Windows, Development, banco loopback e autorização explícita por flag. Não expõem endpoints HTTP nem substituem a autenticação do painel.

## Validação e evidências

- Build Release: zero avisos e erros; migrations 001–027 sequenciais e verificadas.
- **242 testes unitários**, **94 testes de integração local** e **5 testes do coletor Python** passaram. A matriz responsiva consolidada passou em **200 casos**, sem overflow, erro JavaScript ou violação axe nos pontos examinados. Também passaram 7 jornadas de receitas, 10 de estabelecimentos reais, 40 de idiomas, 6 de navegação regional e 5 de privacidade: **268 verificações no navegador** ao todo. A integração MySQL 8.4 no CI tem resultado na execução vinculada à entrega.
- Integração local usa seleção segura para o banco operacional. A suíte de concorrência/claim de filas roda no banco descartável do CI.
- Responsividade: 360, 390, 768, 1024 e 1440 px; axe nas páginas indicadas pelos scripts; inspeção visual de screenshots. São verificações automatizadas e inspeção amostral, não certificação integral de acessibilidade.
- A primeira rodada de 200 casos encontrou `label-content-name-mismatch` na arte clicável dos cards de receitas. Rótulos corrigidos para conter o texto visível, com repetição dos casos afetados.
- Artefatos locais, ignorados no Git: `artifacts/sprint19/build.log`, `unit.trx`, `integration.trx`, `collector-tests.log`, `visual/report.json`, `recipes/report.json`, `live-catalog/report.json`, `locales/report.json`, `expedition/report.json`, `visit-interactions.json`, snapshots e screenshots. Fixtures de teste são identificadas e removidas do banco; screenshots autenticados vêm de host de teste com papéis controlados.

## Operação local e pendências externas

Aplicação local: `http://localhost:5180`. Receitas: `/receitas/tacaca-em-casa`; operação: `/admin/operacao`; moderação: `/admin/moderacao`; receitas administrativas: `/admin/receitas`; cobertura: `/admin/cobertura`.

Para retomar o coletor com o mesmo ledger/checkpoint:

```powershell
./scripts/expand-food-catalog.ps1 -Output artifacts/sprint18/receita-expanded -Target 100000 -BatchPages 10
```

Executar uma única instância por diretório de coleta. Há backoff e pausa recuperável diante de 429; o número final depende da disponibilidade da fonte e das empresas elegíveis. Não há promessa de obter 100 mil estabelecimentos gastronômicos ativos e únicos.

Na primeira passagem estadual, consultas ao Overpass para novos municípios também registraram timeout e HTTP 502; os jobs foram reagendados. O sucesso do snapshot de Belém não significa sincronização concluída dos 144 municípios.

Pendências reais: disponibilidade do Overpass para completar a passagem estadual; chave válida da Places API (New); habilitação/credencial do Google Routes para homologação; feeds oficiais GTFS/linhas/horários/tarifas e dados fluviais vigentes; revalidação de feeds do crawler antes de reativá-los; geocodificação de cadastros sem coordenadas; moderação/deduplicação e conferência de funcionamento; fontes oficiais e autorização para futuras receitas oficiais; configuração pessoal das três contas; instalação do Worker como serviço e implantação HTTPS/IIS no servidor de produção. Nenhuma dessas integrações foi simulada como concluída.

## Arquivos e componentes

Controllers novos: `RecipesController`, `AdminRecipesController`. Alterados: `AdminWorkerController`, `EstablishmentController`, `HomeController`, `VisitController`.

Services/repositórios novos: `IParaRecipes`, `MySqlParaRecipes`, `MySqlStatewideDiscoveryPlan`, `MySqlMaintenanceJobs`, `MaintenanceHostedService`, `VisitWindows`. Alterados: `MySqlBulkModeration`, `MySqlCrawlerTaskQueue`, `RobotsTxtService`, `OpeningHours`, DI e Program do Worker. Helpers locais/evidência: `EditorialHomologation`, `OperationEvidence`.

Views novas: `Recipes/Index`, `Recipes/Details`, `AdminRecipes/Index`, `AdminRecipes/Edit`, `Shared/_RecipeCard`. Alteradas: `AdminBulk/Index`, `AdminBulk/Result`, `AdminWorker/Index`, `Establishment/Details`, `Home/Index`, `Shared/_Layout`, `Shared/_AdminLayout`, `Visit/Index`.

CSS/JS: novos `para-recipes.css`, `recipes.js`, `bulk-moderation.js`; alterados `para.css`, `para.js`. Recursos de localização atualizados nas nove variantes existentes.

Migrations novas e imutáveis depois de aplicadas: **025** receitas/versões/relações/etapas/jobs; **026** índices de moderação/proveniência; **027** pausa auditada das quatro URLs iniciais não confirmadas. Não foram reescritas migrações antigas.

Testes novos: `RecipeAndVisitWindowTests`, `CrawlerRobotsTests`, `RecipeEditorialTests`, `CrawlerQueueTests`. Ajustados: `CuiaExperienceV5Tests`, `BulkAndVisitTests`. Scripts novos: `verify-recipes.cjs`, `verify-live-catalog.cjs`; ajustados: `verify-responsive.cjs`, `verify-visit-interactions.cjs`. A lista completa de caminhos está no diff do commit da entrega.

### Relação completa de caminhos desta entrega

```text
README.md
docs/SPRINT19-REPORT.md
scripts/verify-live-catalog.cjs
scripts/verify-recipes.cjs
scripts/verify-responsive.cjs
scripts/verify-visit-interactions.cjs
src/BelemRestaurantes.Application/Abstractions/IParaRecipes.cs
src/BelemRestaurantes.Application/Models/BulkModeration.cs
src/BelemRestaurantes.Application/Models/ParaRecipes.cs
src/BelemRestaurantes.Application/Models/VisitPlanning.cs
src/BelemRestaurantes.Application/Services/OpeningHours.cs
src/BelemRestaurantes.Application/Services/VisitWindows.cs
src/BelemRestaurantes.Infrastructure/BelemRestaurantes.Infrastructure.csproj
src/BelemRestaurantes.Infrastructure/Crawler/MySqlCrawlerTaskQueue.cs
src/BelemRestaurantes.Infrastructure/Crawler/RobotsTxtService.cs
src/BelemRestaurantes.Infrastructure/Editorial/para-recipes.json
src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs
src/BelemRestaurantes.Infrastructure/Migrations/Scripts/025_para_recipes_and_review_stages.sql
src/BelemRestaurantes.Infrastructure/Migrations/Scripts/026_moderation_lookup_indexes.sql
src/BelemRestaurantes.Infrastructure/Migrations/Scripts/027_pause_unverified_seed_feeds.sql
src/BelemRestaurantes.Infrastructure/MySql/MySqlBulkModeration.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlMaintenanceJobs.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlParaRecipes.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlStatewideDiscoveryPlan.cs
src/BelemRestaurantes.Web/Controllers/AdminRecipesController.cs
src/BelemRestaurantes.Web/Controllers/AdminWorkerController.cs
src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs
src/BelemRestaurantes.Web/Controllers/HomeController.cs
src/BelemRestaurantes.Web/Controllers/RecipesController.cs
src/BelemRestaurantes.Web/Controllers/VisitController.cs
src/BelemRestaurantes.Web/Resources/SharedResource.de.resx
src/BelemRestaurantes.Web/Resources/SharedResource.en.resx
src/BelemRestaurantes.Web/Resources/SharedResource.es.resx
src/BelemRestaurantes.Web/Resources/SharedResource.fr.resx
src/BelemRestaurantes.Web/Resources/SharedResource.ja.resx
src/BelemRestaurantes.Web/Resources/SharedResource.pt-BR.resx
src/BelemRestaurantes.Web/Resources/SharedResource.resx
src/BelemRestaurantes.Web/Resources/SharedResource.ru.resx
src/BelemRestaurantes.Web/Resources/SharedResource.zh-Hans.resx
src/BelemRestaurantes.Web/Views/AdminBulk/Index.cshtml
src/BelemRestaurantes.Web/Views/AdminBulk/Result.cshtml
src/BelemRestaurantes.Web/Views/AdminRecipes/Edit.cshtml
src/BelemRestaurantes.Web/Views/AdminRecipes/Index.cshtml
src/BelemRestaurantes.Web/Views/AdminWorker/Index.cshtml
src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml
src/BelemRestaurantes.Web/Views/Home/Index.cshtml
src/BelemRestaurantes.Web/Views/Recipes/Details.cshtml
src/BelemRestaurantes.Web/Views/Recipes/Index.cshtml
src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml
src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml
src/BelemRestaurantes.Web/Views/Shared/_RecipeCard.cshtml
src/BelemRestaurantes.Web/Views/Visit/Index.cshtml
src/BelemRestaurantes.Web/wwwroot/css/para-recipes.css
src/BelemRestaurantes.Web/wwwroot/css/para.css
src/BelemRestaurantes.Web/wwwroot/js/bulk-moderation.js
src/BelemRestaurantes.Web/wwwroot/js/para.js
src/BelemRestaurantes.Web/wwwroot/js/recipes.js
src/BelemRestaurantes.Worker/EditorialHomologation.cs
src/BelemRestaurantes.Worker/MaintenanceHostedService.cs
src/BelemRestaurantes.Worker/OperationEvidence.cs
src/BelemRestaurantes.Worker/Program.cs
tests/BelemRestaurantes.IntegrationTests/BulkAndVisitTests.cs
tests/BelemRestaurantes.IntegrationTests/CrawlerQueueTests.cs
tests/BelemRestaurantes.IntegrationTests/RecipeEditorialTests.cs
tests/BelemRestaurantes.UnitTests/CrawlerRobotsTests.cs
tests/BelemRestaurantes.UnitTests/CuiaExperienceV5Tests.cs
tests/BelemRestaurantes.UnitTests/RecipeAndVisitWindowTests.cs
```
