# Sprint 14 — mais 361 estabelecimentos reais de Belém

Validação local em 18/10/2026, America/Sao_Paulo.

**Resultado:** o banco passou de **120 para 481 estabelecimentos**, um acréscimo real de **361**, acima dos 300 solicitados. Todos estão `pending`, disponíveis na triagem administrativa. Nenhuma aprovação em massa foi executada; os cadastros aparecem na busca pública depois da moderação.

## Evidência da importação

Fonte: OpenStreetMap, através de uma consulta Overpass limitada à fronteira administrativa de Belém/PA. Coleta concluída em **18/10/2026 às 08:06:36 UTC** (05:06:36 em Brasília).

| Medida | Quantidade |
| --- | ---: |
| Catálogo antes desta entrega | 120 |
| Elementos recebidos no snapshot | 485 |
| Lugares elegíveis no parser | 483 |
| Escritas novas confirmadas | 361 |
| Atualizações/reencontros na importação | 121 |
| Descarte por nome genérico sem identidade (`BAR`) | 1 |
| Falhas de ingestão | 0 |
| Catálogo distinto após deduplicação | **481** |

Contagens de elementos da fonte, escritas e restaurantes distintos medem coisas diferentes. A deduplicação reutiliza o registro existente quando identifica o mesmo lugar. Uma segunda execução **sem nova chamada externa** confirmou **zero novos, 482 atualizações/reencontros e zero falhas**, mantendo 481 registros distintos.

As fixtures de testes são removidas ao final e não integram esses números. A descoberta prova a existência de registros na fonte, sem confirmar funcionamento atual, qualidade, horários ou autorização de fotos.

## Distribuição do catálogo persistido

| Tipo | Estabelecimentos |
| --- | ---: |
| Restaurante | 181 |
| Bar | 70 |
| Lanchonete | 37 |
| Cafeteria | 28 |
| Hamburgueria | 26 |
| Padaria | 26 |
| Feira e mercado | 23 |
| Confeitaria | 20 |
| Pizzaria | 19 |
| Sorveteria | 19 |
| Açaí | 17 |
| Hortifruti | 4 |
| Praça de alimentação | 4 |
| Tacacá | 4 |
| Peixaria | 2 |
| Empório | 1 |
| **Total** | **481** |

Todos pertencem ao município de Belém e aguardam revisão. As categorias refletem tags e nomes da fonte, com regras determinísticas; não constituem classificação editorial definitiva. Feiras e mercados usam a tag [amenity=marketplace](https://wiki.openstreetmap.org/wiki/Tag:amenity%3Dmarketplace); tipos de cozinha usam a [documentação de cuisine](https://wiki.openstreetmap.org/wiki/Key:cuisine).

## Evolução implementada

- Removido o corte de 120 lugares no parser e na consulta. O novo limite operacional é **2.000 elementos por município**, mantendo limite de resposta, timeout e cache durável de 24 horas. Não se afirma cobertura exaustiva de Belém.
- Incluídas feiras, praças de alimentação, padarias, confeitarias, hortifrútis, peixarias e outros comércios alimentares, além das categorias anteriores. Lojas sem relação com alimentação e lugares explicitamente desativados são descartados.
- Açaí, tacacá e pizza podem ser reconhecidos por tags de cozinha ou termos claros no nome. Tags de cozinha com múltiplos valores são interpretadas. Coordenadas inválidas e municípios divergentes são recusados.
- Novo comando de importação pontual por município oficial, usando o mesmo orquestrador, validação, deduplicação, proveniência e moderação do Worker. A execução registra um ciclo real e resultado por cidade/fonte sem consumir jobs agendados. Repetir com cache não repete a coleta externa.
- Diagnóstico `--verify-discovery` agora inclui distribuição por tipo/município e histórico das importações manuais.
- Home com oito cartões de categorias e novos ícones locais de pizza, pão, café, lanche e sorvete. Busca ganhou atalhos por categoria e preserva a seleção mesmo quando ainda não existem publicados daquela categoria.
- Triagem administrativa reconstruída em cartões com tipo, bairro, endereço, contato, identificador, data e link da fonte. Busca por nome/endereço/bairro, filtros por município e categoria e paginação preservando filtros. Rejeição exige motivo no formulário; aprovação continua individual.
- Consultas de triagem são parametrizadas e escapam curingas de busca. Não há novo bypass de autenticação nem exposição pública de registros pendentes.

## Migration

**020_manual_discovery_runs.sql**, aplicada no MySQL local: permite `job_id` nulo na telemetria por alvo para importações pontuais. Mantém a chave estrangeira para jobs agendados. Os 19 scripts anteriores não foram alterados nesta entrega.

## Validação

- Build Release: **zero erros e avisos**.
- **178 testes unitários aprovados**, incluindo snapshot com 500 itens sem truncamento, deduplicação de IDs, categorias ampliadas, tags de cozinha múltiplas e descarte de lojas alheias/desativadas.
- **55 testes de integração selecionados aprovados**, incluindo telemetria manual sem job, triagem com filtros/paginação/SQL parametrizado, contatos de revisão e fluxos anteriores de segurança e publicação controlada.
- **105 casos responsivos aprovados**, sem overflow, erros JavaScript ou violações nas regras axe selecionadas. Revisão em 360, 390, 768, 1024 e 1440 px: páginas públicas, snapshots autenticados e exemplos preenchidos de homologação. Resultado final registrado em `artifacts/sprint14/responsive-summary.json`.
- Axe verifica regras WCAG A/AA selecionadas em 360 px. Testes automatizados não equivalem a certificação integral de acessibilidade ou pentest.
- Web e Worker executados separadamente em Development. Os ciclos normais continuam a cada minuto com cache, sem repetir a consulta externa a cada ciclo. Não foram executados testes que consomem a fila operacional.
- Artefatos Release preparados em `artifacts/sprint14/publish/Web` e `artifacts/sprint14/publish/Worker`; implantação externa não executada.

## Evidências locais

- `artifacts/sprint14/baseline-evidence.json`: evidência anterior com 120 pendentes.
- `artifacts/sprint14/import-belem.log`: coleta HTTP 200 e importação com 361 criações, sem falhas.
- `artifacts/sprint14/import-repeat.log`: segunda execução idempotente, sem nova coleta, com zero criações.
- `artifacts/sprint14/discovery-evidence.json`: catálogo, categorias, municípios e ciclos consultados no banco.
- `artifacts/sprint14/catalogo-revisao.csv`: os 481 registros persistidos, com identificação/fonte/licença, para revisão local. Não inclui dados privados de usuários ou parceiros. Exportação textual; contatos continuam sujeitos à conferência.
- `artifacts/sprint14/open-data-export.json`: snapshot bruto da fonte, também disponível em `/dados-abertos/openstreetmap`.
- `artifacts/sprint14/build.log`, `unit.trx`, `integration.trx`, `integration.log`, `worker.log`, `web.log`, `visual/report.json`, screenshots e `responsive-summary.json`.

## Reproduzir a importação

Com conexão MySQL em User Secrets do Worker ou variável de ambiente e `Discovery:Provider=OpenStreetMap`:

```powershell
# Reutiliza a coleta da cidade por até 24h; não publica os resultados.
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --import-open-data --city=Belém

# Consulta somente leitura, sem valores de credenciais.
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --verify-discovery
```

`--refresh-open-data` força uma coleta pontual e deve ser reservado à operação explícita. Não usar em laço. Para produção contínua, configurar instância Overpass própria por `OpenData:Endpoint`, conforme a [política oficial do serviço público](https://dev.overpass-api.de/overpass-doc/en/preface/commons.html). Mantidas atribuição e exportação segundo [OpenStreetMap/ODbL](https://www.openstreetmap.org/copyright).

## Pendências remanescentes

- Conferência humana dos 481 cadastros antes da publicação e obtenção de fotos autorizadas.
- Chave Places API (New) válida, caso seja escolhido o provedor Google; não bloqueia esta importação aberta.
- Rotação de credenciais expostas historicamente e implantação/configuração da VPS, incluindo fonte própria para produção.

## Arquivos desta evolução

Além da documentação/README, foram envolvidos:

- `src/BelemRestaurantes.Infrastructure/OpenData/OpenStreetMapDiscoveryClient.cs`
- `src/BelemRestaurantes.Domain/Rules/ValidationRules.cs`
- `src/BelemRestaurantes.Application/Abstractions/IRepositories.cs`
- `src/BelemRestaurantes.Application/Models/RestaurantView.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlModerationRepository.cs`
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryTargetRepository.cs`
- `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/020_manual_discovery_runs.sql`
- `src/BelemRestaurantes.Worker/Program.cs`
- `src/BelemRestaurantes.Worker/OpenDataImport.cs`
- `src/BelemRestaurantes.Worker/DiscoveryEvidence.cs`
- `src/BelemRestaurantes.Web/Controllers/AdminOperationsController.cs`
- `src/BelemRestaurantes.Web/Models/CatalogCategories.cs`
- `src/BelemRestaurantes.Web/Views/Home/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/Search/Index.cshtml`
- `src/BelemRestaurantes.Web/Views/AdminAccount/Discovery.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_CategoryShortcuts.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_DiscoveryReview.cshtml`
- `src/BelemRestaurantes.Web/Views/Shared/_ParaIcon.cshtml`
- `src/BelemRestaurantes.Web/wwwroot/css/para.css`
- `src/BelemRestaurantes.Web/wwwroot/css/para-categories.css`
- `tests/BelemRestaurantes.UnitTests/OpenDataDiscoveryTests.cs`
- `tests/BelemRestaurantes.UnitTests/CuiaExperienceV5Tests.cs`
- `tests/BelemRestaurantes.IntegrationTests/DiscoveryOperationsTests.cs`
- `tests/BelemRestaurantes.IntegrationTests/CatalogJourneyTests.cs`
