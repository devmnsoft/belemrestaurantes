# Sprint 12 — Discovery ativo e identidade Pará

Data da validação: 17/10/2026 (America/Sao_Paulo).

**Status: entrega de código validada; critério de descoberta real ainda não concluído.** O Worker inicia em Development, separado da Web, com Discovery e Crawler habilitados e intervalo de um minuto. A credencial disponível para Google Places é um segredo OAuth e a chamada real retorna HTTP 400 (`API key not valid`). Não foram descobertos ou persistidos restaurantes reais nesta execução. O catálogo consultado estava vazio. Dados controlados de testes não são contabilizados como descobertas.

## Correções do robô

- O host usava o diretório de trabalho da chamada e não encontrava seus appsettings. O Worker agora usa `AppContext.BaseDirectory`; seus JSONs são copiados para build/publicação. User Secrets próprios foram confirmados em Development.
- Os mínimos fixos de dez e cinco minutos ignoravam a configuração de um minuto. Ambos aceitam agora um minuto. Development usa lote 2 para Discovery e 3 tarefas para Crawler. A atualização de cada alvo pode voltar à fila após um minuto em Development; o padrão de produção continua semanal por alvo.
- `new Uri(base, "places:searchText")` interpretava `places` como esquema. A URL relativa explícita `./places:searchText` produz o endpoint HTTPS correto.
- O cliente não mapeava município/bairro. O validador rejeitava todos os resultados por município ausente. A máscara e o parser incluem os componentes do endereço, telefone, estado e situação operacional. Details deixa de enviar `nextPageToken`, campo exclusivo de busca.
- Corrigida a faixa de latitude que descartava municípios do sul do Pará. Quando o estado é fornecido pela fonte, deve ser Pará/PA. A faixa de coordenadas é uma verificação aproximada, não um teste de polígono geográfico.
- As chaves de IA agora são lidas de `Ai:{Provider}:ApiKey` em User Secrets/variáveis hierárquicas, além dos aliases de ambiente já suportados.
- Credenciais foram retiradas dos appsettings versionados e preservadas nos User Secrets locais de cada projeto. O histórico Git ainda exige rotação das credenciais expostas; não foi reescrito.
- Diagnóstico de inicialização informa presença/ausência das cinco configurações pedidas sem imprimir os valores. MySQL, Google, Gemini e Groq estavam presentes; DeepSeek estava ausente. Presença não prova validade de uma credencial.
- Telemetria diferencia quantidade retornada pela fonte, inserções, atualizações, descartes e falhas. A contagem de persistidos é feita após o commit, mesmo se houver falha posterior na observação/moderação. O resultado de INSERT/UPDATE vem da persistência, não da similaridade do nome.
- Deduplicação considera Place ID e nome/cidade/endereço exatos. Filiais de endereços diferentes permanecem separadas. Ingestões são serializadas com lock nomeado do MySQL para evitar corrida entre fontes; o lock é liberado em `finally`.
- Dados de parceiros com vínculo verificado não são sobrescritos pela automação. Telefone, redes sociais válidas e situação operacional são persistidos nas colunas existentes.
- IDs de extração do Crawler usam SHA-256 determinístico por fonte/nome/cidade/endereço, substituindo `GetHashCode`, que variava entre processos e podia colidir para estabelecimentos na mesma página.
- O enfileiramento de URLs filhas é aguardado. A consulta inicial às fontes também verifica robots.txt e registra falhas por cidade/fonte. Permanecem as proteções SSRF do cliente seguro.
- Tarefas processadas pelo Crawler deixaram de ser reportadas como restaurantes criados ou unidades de API consumidas. Os números reais de extração e ingestão estão em `crawler_runs` e no painel `/admin/crawler`.
- O construtor da conexão respeita o ambiente efetivo do host, inclusive quando informado por argumento; a exceção de TLS permanece limitada a loopback em Development.

## Banco e migrations

- **Nova: 017_discovery_target_runs.sql.** Persiste métricas por ciclo/job/cidade/categoria/fonte. Aplicada no banco local.
- **Existente: 016_cuia_discovery_belem_activation.sql.** Estava pendente; foi aplicada na inicialização autorizada do Worker, habilitando os alvos de Belém e as fontes já previstas no script.
- As migrations 001–016 não foram editadas. A verificação de checksum agora aceita as representações LF/CRLF do mesmo SQL. Alteração de conteúdo continua rejeitada. `.gitattributes` fixa LF para SQL.
- O painel Discovery mostra ciclos e consultas por cidade, além da triagem para publicação. Ausência de registros é mostrada como ausência de evidência, sem números fictícios.
- Novos restaurantes continuam `pending`. Publicação depende da moderação humana existente, com auditoria.
- Corrigidas materializações Dapper de `CrawlerSummaryStats` (COUNT Int64/SUM Decimal) e `Restaurant` (construtor de domínio incompatível com SELECT). Testes autenticados reproduziram os erros anteriores e verificaram a correção.

## Interface entregue

- Home reconstruída com composição editorial, verde amazônico, tucupi, urucum, azul-rio e palha; imagem grande de Belém, pesquisa, categorias, catálogo, destinos, comunidade, mobilidade e convite aos parceiros.
- Arte ilustrada original, identificada como ilustração; não representa foto de um restaurante real.
- Ícones SVG locais de cuia, folha, peixe, rio, mercado, prato e mobilidade.
- Novo cabeçalho, menu mobile com Escape, rodapé, seletor de idioma e links de salto para o conteúdo.
- Cards compartilhados por home, busca e favoritos. Sem foto, aparece um estado visual explicitamente identificado, sem usar a arte gerada como evidência do estabelecimento.
- Busca/autocomplete com debounce, cancelamento de chamadas antigas, carregamento, vazio, erro, navegação por teclado e inserção de texto via DOM seguro. URLs de sugestões ficam restritas à mesma origem.
- Novas páginas públicas `/receitas` e `/selo-cuia`. O ranking usa o serviço e os registros reais existentes; não há ranking de demonstração.
- Login, cadastro, recuperação/redefinição de senha e login administrativo receberam composição de acesso comum. Perfil tem link para favoritos e associações de labels corrigidas.
- Nova página `/account/favorites`, com adição/remoção por POST e antiforgery, sempre no sujeito autenticado. Restaurantes não publicados não são exibidos na lista.
- Alias `/estabelecimento/{id}` corrigiu links que não correspondiam à rota original `/establishment/{id}`.
- Comunidade, cidades/localidades, categorias, restaurante, roteiros/mobilidade, parceiro e administrativo recebem a base visual comum e correções de grades que excediam a largura mobile.
- A base CSS tem entrada única `para.css`, que importa as folhas de compatibilidade dos módulos existentes. Não se afirma remoção completa do CSS legado ou de todos os estilos inline.
- Scripts inline de experiências e handlers de copiar/confirmar foram migrados para arquivos externos, compatíveis com a CSP. Dados Razor passam por atributos HTML, evitando interpolação em JavaScript. Nenhum `unsafe-inline` foi adicionado a scripts.
- Home carrega módulos de forma independente: falha de ranking, comunidade ou roteiro não elimina os restaurantes que puderam ser carregados.

## Validação e evidências

- Build Release da solução: sem erros ou avisos.
- Unitários: **157 aprovados**.
- Integração selecionada: **48 aprovados** (migrations, localização, endpoints públicos, segurança, catálogo e telas autenticadas de operação/perfil/favoritos/parceiro).
- Jornada controlada no MySQL verifica INSERT/UPDATE, deduplicação entre fontes, separação de filiais, contatos, proteção de dados do parceiro, moderação, publicação e auditoria. Os registros de fixture são removidos ao final e não constituem restaurantes descobertos.
- Foram testadas publicamente home, busca, cidades, comunidade, roteiros, receitas, Selo Cuia, login, cadastro, recuperação e login administrativo em 360, 390, 768, 1024 e 1440 px: **55 casos sem overflow, sem erro JavaScript, HTTP 200**.
- Na primeira auditoria automatizada foram corrigidos nome acessível da marca, semântica do autocomplete e contraste do texto tucupi. A repetição desses 55 casos não apontou violações nas regras WCAG A/AA selecionadas do axe.
- Teste de menu mobile, Escape, teclado do autocomplete e resposta maliciosa de sugestões passou. O payload de HTML aparece como texto; a URL JavaScript é descartada.
- A verificação visual final reuniu **85 casos aprovados**: os 55 casos públicos e 30 renderizações de snapshots autenticados (Discovery, Crawler, Discovery 360, perfil, favoritos e Portal Parceiro) nas mesmas cinco larguras. As áreas de tabela receberam foco por teclado, nome acessível e ajustes de contraste. Zero violações nas regras A/AA selecionadas do axe nas páginas de 360 px; zero overflow ou erros JavaScript em todas as larguras.
- As telas autenticadas são verificadas no host de integração com identidade de teste. Seus snapshots HTML podem ser renderizados pelo script visual; a aplicação real não recebeu bypass de autenticação.
- A auditoria automatizada e as verificações realizadas não equivalem a certificação integral WCAG 2.2 AA ou pentest completo de todos os fluxos autenticados.

Arquivos locais de evidência (ignorados pelo Git):

- `artifacts/test-results/worker-development.log`: primeiras chamadas corrigidas e resposta real HTTP 400 do Google.
- `artifacts/test-results/worker-final-development.log`: host final, ambiente, intervalos e ciclos reais. Foram observados inícios às 13:54:43, 13:55:43, 13:56:43 e 13:57:43 (Brasília), registrados no banco às 16:54:43–16:57:43 UTC. Em todos, zero encontrados/criados e duas falhas de consulta por ciclo.
- `artifacts/test-results/discovery-evidence.json`: consulta somente leitura a catálogo, ciclos, resultados por cidade e Crawler.
- `artifacts/test-results/sprint12-unit.trx` e `sprint12-integration.trx`.
- `artifacts/test-results/visual/report.json`, `responsive-summary.json` e PNGs por tela/largura.
- `artifacts/test-results/admin-snapshots/*.html`: snapshots autorizados pelo host de teste, sem credenciais reais.

## Reproduzir

```powershell
dotnet build BelemRestaurantes.sln -c Release
dotnet run --project src/BelemRestaurantes.Worker -c Release -- --environment Development
# Diagnóstico de dados, sem iniciar o robô ou aplicar migrations:
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-launch-profile -- --environment Development --verify-discovery
# Portal independente:
dotnet run --project src/BelemRestaurantes.Web -c Release --no-launch-profile -- --environment Development --urls http://localhost:5180
```

Configure segredos no projeto correto. O Worker usa `UserSecretsId=belemrestaurantes-worker`; a Web usa `belemrestaurantes-web`. Alternativa: `ConnectionStrings__MySql`, `GooglePlaces__ApiKey`, `Ai__Gemini__ApiKey`, `Ai__Groq__ApiKey`, `Ai__DeepSeek__ApiKey`. User Secrets só são carregados automaticamente em Development. Não envie chaves pelo chat nem as coloque em arquivos versionados. Para homologação com outro nome de ambiente, use variáveis de ambiente e defina explicitamente os intervalos de ambos os serviços.

```powershell
# Exemplo: substituir o placeholder no terminal local.
dotnet user-secrets set 'GooglePlaces:ApiKey' 'CHAVE_VALIDA_PLACES_NEW' --project src/BelemRestaurantes.Worker
# Reinicie o Worker após modificar segredos.
```

Os jobs que já esgotaram as tentativas devem ser revistos/reagendados no diagnóstico administrativo após corrigir a chave. Ativar um alvo não remove automaticamente uma dead-letter; isso evita repetir uma falha permanente indefinidamente.

Para verificar os testes de integração, informe `BELRESTAURANTES_TEST_MYSQL` com um banco autorizado de desenvolvimento. Não execute testes que reservam jobs existentes (como concorrência de fila) contra uma fila operacional compartilhada. Eles não fizeram parte desta validação selecionada.

Para a verificação visual:

```powershell
npm install --prefix artifacts/ui-test playwright @axe-core/playwright --no-audit --no-fund
# Instale Chromium pelo Playwright se ainda não estiver disponível.
node scripts/verify-responsive.cjs
```

## Arte

Ferramenta: imagegen integrada, conforme skill imagegen. Arquivo final no projeto: `src/BelemRestaurantes.Web/wwwroot/images/belem-sabores-editorial.png`.

Prompt final: “Create one wide landscape editorial illustration for a premium but welcoming local food guide website called Belém Restaurantes. NO TEXT, NO LETTERS, NO LOGOS. Landscape 3:2 composition, richly detailed painterly screenprint meets travel editorial art with subtle paper grain. View of Belém do Pará Brazil at warm golden late afternoon: distinctive Ver-o-Peso blue iron market towers at right, ochre waterfront warehouses inspired by Estação das Docas, Guajará Bay with small regional boats, lush Amazon foliage framing top corners. Foreground: traditional dark decorated cuia bowl of yellow tacacá with jambu leaves and shrimp, dark purple açaí in a bowl, golden fried fish on regional ceramic plate. Color palette deep Amazon green, tucupi gold, urucum red, river blue, warm straw cream. Sophisticated confident shapes, authentic Northern Brazilian atmosphere, enticing food, warm human spirit. Illustration, not a photograph; no restaurant business or signage. Full bleed with strong readable focal composition at center and right.”

## Pendências que impedem encerrar a sprint

1. Configurar uma chave válida da Places API (New), com API habilitada e faturamento/permissões adequados; reiniciar o Worker e reagendar os jobs com falha.
2. Validar resultados externos reais persistidos no banco, moderar/publicar registros e comprovar a exibição pública. **Quantidade comprovada de restaurantes reais descobertos nesta sessão: zero.**
3. Revisar as fontes atuais do Crawler: houve falhas de DNS, TLS e fontes sem dados úteis. As proteções TLS/SSRF/robots não foram contornadas para obter resultados.
4. Configurar DeepSeek caso esse fallback seja necessário; presença de Gemini/Groq não valida as respectivas chaves.
5. Revogar/rotacionar as credenciais anteriormente versionadas. Removê-las do checkout não as remove do histórico.
6. Completar revisão manual de acessibilidade e validação de fluxos com usuários, restaurantes, fotos e avaliações reais. A cobertura visual autenticada usa estados atuais do banco, principalmente vazios.
7. Publicação em IIS/Windows Service permanente não foi executada. A execução validada é local em Development, com Web e Worker em processos separados.

## Manifesto de arquivos alterados

- `.env.example`
- `.gitattributes`
- `README.md`
- `docs/SPRINT12-REPORT.md`
- `scripts/verify-responsive.cjs`
- `src/BelemRestaurantes.Application/Abstractions/ICrawlerServices.cs`
- `src/BelemRestaurantes.Application/Abstractions/IRepositories.cs`
- `src/BelemRestaurantes.Application/Models/RestaurantView.cs`
- `src/BelemRestaurantes.Application/Services/CrawlerOrchestrator.cs`
- `src/BelemRestaurantes.Application/Services/DiscoveryOrchestrator.cs`
- `src/BelemRestaurantes.Application/Services/PlaceValidator.cs`
- `src/BelemRestaurantes.Domain/Entities/Restaurant.cs`
- `src/BelemRestaurantes.Infrastructure/Google/GooglePlacesClient.cs`
- `src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- `src/BelemRestaurantes.Infrastructure/Migrations/MigrationRunner.cs`
- `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/017_discovery_target_runs.sql`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlConnectionFactory.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryQueue.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryTargetRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlRestaurantIngestionRepository.cs`
- `src/BelemRestaurantes.Web/Controllers/AccountController.cs`
- `src/BelemRestaurantes.Web/Controllers/AdminOperationsController.cs`
- `src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs`
- `src/BelemRestaurantes.Web/Controllers/HomeController.cs`
- `src/BelemRestaurantes.Web/Models/DiscoveryOperationsView.cs`
- `src/BelemRestaurantes.Web/Views/Account/Email.cshtml`
- `src/BelemRestaurantes.Web/Views/Account/Favorites.cshtml`
- `src/BelemRestaurantes.Web/Views/Account/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Account/Login.cshtml`
- `src/BelemRestaurantes.Web/Views/Account/Register.cshtml`
- `src/BelemRestaurantes.Web/Views/Account/Reset.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Crawler.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Cuia.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Dashboard.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Discovery.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Discovery360.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Lgpd.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Login.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Members.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Photos.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminPartner/Claims.cshtml`
- `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml`
- `src/BelemRestaurantes.Web/Views/Experience/Create.cshtml`
- `src/BelemRestaurantes.Web/Views/Experience/Details.cshtml`
- `src/BelemRestaurantes.Web/Views/Experience/Feed.cshtml`
- `src/BelemRestaurantes.Web/Views/Home/Cuia.cshtml`
- `src/BelemRestaurantes.Web/Views/Home/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Home/Recipes.cshtml`
- `src/BelemRestaurantes.Web/Views/Menu/ManageMenu.cshtml`
- `src/BelemRestaurantes.Web/Views/Partner/Announcements.cshtml`
- `src/BelemRestaurantes.Web/Views/Partner/OrderConfirmation.cshtml`
- `src/BelemRestaurantes.Web/Views/Search/Cities.cshtml`
- `src/BelemRestaurantes.Web/Views/Search/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_AuthStory.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_ParaIcon.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_RestaurantCard.cshtml`
- `src/BelemRestaurantes.Web/appsettings.json`
- `src/BelemRestaurantes.Web/wwwroot/css/para.css`
- `src/BelemRestaurantes.Web/wwwroot/images/belem-sabores-editorial.png`
- `src/BelemRestaurantes.Web/wwwroot/js/experience.js`
- `src/BelemRestaurantes.Web/wwwroot/js/para.js`
- `src/BelemRestaurantes.Web/wwwroot/js/site.js`
- `src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj`
- `src/BelemRestaurantes.Worker/ConfigurationDiagnosticsService.cs`
- `src/BelemRestaurantes.Worker/CrawlerHostedService.cs`
- `src/BelemRestaurantes.Worker/DiscoveryEvidence.cs`
- `src/BelemRestaurantes.Worker/DiscoveryHostedService.cs`
- `src/BelemRestaurantes.Worker/Program.cs`
- `src/BelemRestaurantes.Worker/appsettings.Development.json`
- `src/BelemRestaurantes.Worker/appsettings.json`
- `tests/BelemRestaurantes.IntegrationTests/CatalogJourneyTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/DiscoveryOperationsTests.cs`
- `tests/BelemRestaurantes.UnitTests/CuiaExperienceV5Tests.cs`
- `tests/BelemRestaurantes.UnitTests/DiscoveryActivationTests.cs`
