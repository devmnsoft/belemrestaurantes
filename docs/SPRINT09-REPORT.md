# Relatório de Conclusão — SPRINT 09: Cuia Discovery 360
**Cobertura Estadual, Inteligência de Dados, Segurança e Experiência Gastronômica**

* **Produto:** Belém Restaurantes — O Pará tem sabor
* **Empresa:** MNSOFT Soluções em Tecnologia
* **GitHub:** [https://github.com/devmnsoft/belemrestaurantes](https://github.com/devmnsoft/belemrestaurantes)
* **Stack:** ASP.NET Core 10 (`net10.0`), Razor/MVC, Dapper, MySQL `belrestaurante`, IIS 10, Windows Service `BelemRestauranteCuiaDiscovery`
* **Branch:** `sprint09-cuia-discovery-360`
* **Data:** 17 de Outubro de 2026

---

## 1. Sumário Executivo & Missão Cumprida

A Sprint 09 evoluiu o **Cuia Discovery** e o **Cuia Crawler** para o ecossistema autônomo **Cuia Discovery 360**, estabelecendo cobertura geográfica estruturada para todos os municípios do Pará (com códigos oficiais do IBGE), enriquecimento contínuo com rastreabilidade de atributos, reconciliação auditável com proteção total aos comerciantes verificados (Tier 1), motor de mídias por tiers de licença e uma central operacional moderna no painel MNSOFT.

Toda a arquitetura existente foi rigorosamente preservada, sem recriação do robô e sem modificações indiscriminadas nas migrações anteriores.

---

## 2. Inventário de Arquivos Criados e Modificados

### A. Migrações e Banco de Dados (MySQL)
* [`src/BelemRestaurantes.Infrastructure/Migrations/Scripts/013_cuia_discovery_360.sql`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/Migrations/Scripts/013_cuia_discovery_360.sql):
  * Tabela `geo_municipalities`: Mapeamento com códigos oficiais do IBGE, mesorregiões, polos turísticos e indicadores de cobertura.
  * Tabela `restaurant_enrichment_queue`: Fila transacional de enriquecimento com suporte a lock atômico.
  * Tabela `restaurant_attribute_history`: Rastreabilidade de cada campo, score de confiança e procedência da fonte.
  * Tabela `restaurant_media`: Galeria estruturada com 4 tiers de prioridade de licença.
  * Seed dos municípios paraenses (Belém, Santarém, Castanhal, Soure, Salinas, Marabá, etc.).

### B. Domínio e Abstrações
* [`src/BelemRestaurantes.Domain/Entities/Discovery360Entities.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Domain/Entities/Discovery360Entities.cs): Entidades `GeoMunicipality`, `MunicipalityCoverageStats`, `RestaurantEnrichmentQueueItem`, `RestaurantAttributeHistory`, `RestaurantMedia` e enums de prioridade e status.
* [`src/BelemRestaurantes.Application/Abstractions/IDiscovery360Services.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Application/Abstractions/IDiscovery360Services.cs): Contratos `IGeoMunicipalityRepository`, `IEnrichmentQueueRepository`, `IRestaurantAttributeHistoryRepository`, `IRestaurantMediaRepository` e `IEnrichmentService`.

### C. Infraestrutura e Persistência
* [`src/BelemRestaurantes.Infrastructure/Crawler/SafeHttpClient.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/Crawler/SafeHttpClient.cs):
  * **Correção DNS Multi-Homed:** Substituição de `.All()` por `.Any()` para bloquear domínios com resolução mista ou rebinding malicioso.
  * **Revalidação em Redirecionamento:** `AllowAutoRedirect = false` com controle manual de saltos (máx. 3) e revalidação de scheme, porta e DNS a cada hop.
  * **Ampliação de Bloqueios:** Loopback, RFC 1918, Link-Local (`169.254.169.254`), CGNAT (`100.64.0.0/10`), multicast, redes de teste e metadados de nuvem.
* [`src/BelemRestaurantes.Infrastructure/MySql/MySqlGeoMunicipalityRepository.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/MySql/MySqlGeoMunicipalityRepository.cs): Consultas e indicadores territoriais reais.
* [`src/BelemRestaurantes.Infrastructure/MySql/MySqlEnrichmentRepositories.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/MySql/MySqlEnrichmentRepositories.cs): Repositórios de fila de enriquecimento com locking atômico, histórico e galeria de mídia.
* [`src/BelemRestaurantes.Infrastructure/Crawler/EnrichmentService.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/Crawler/EnrichmentService.cs): Motor de enriquecimento inteligente com proteção a comerciantes verificados e isolamento contra Prompt Injection.
* [`src/BelemRestaurantes.Application/Services/CrawlerOrchestrator.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Application/Services/CrawlerOrchestrator.cs): Integração autônoma de ingestão -> enriquecimento -> mídias -> telemetria municipal.
* [`src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs): Registro dos novos serviços no contêiner DI.

### D. Camada Web & Painel Administrativo
* [`src/BelemRestaurantes.Web/Controllers/AdminCrawlerController.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/AdminCrawlerController.cs): Acionamento manual desacoplado e proteção contra vazamento de exceções.
* [`src/BelemRestaurantes.Web/Controllers/AdminDiscovery360Controller.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/AdminDiscovery360Controller.cs): Central operacional administrativa para cobertura estadual, lote de enriquecimento e moderação de mídias.
* [`src/BelemRestaurantes.Web/Views/AdminAccount/Discovery360.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/AdminAccount/Discovery360.cshtml): Tela administrativa completa no Design System V3/V4.
* [`src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml): Adição do link Cuia Discovery 360 na navegação.
* [`src/BelemRestaurantes.Web/Controllers/SearchController.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/SearchController.cs): Novas rotas `/cidades`, `/cidade/{slug}` e `/categoria/{slug}`.
* [`src/BelemRestaurantes.Web/Views/Search/Cities.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Search/Cities.cshtml): Vitrine pública por mesorregiões do Pará.
* [`src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs) e [`Details.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml): Galeria categorizada por tiers e selo de transparência de procedência.

### E. Worker Windows & Testes
* [`src/BelemRestaurantes.Worker/CrawlerHostedService.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Worker/CrawlerHostedService.cs): Execução cíclica em background tanto de descoberta quanto de enriquecimento contínuo.
* [`tests/BelemRestaurantes.UnitTests/CuiaDiscovery360Tests.cs`](file:///c:/MNSOFT/belemrestaurantes/tests/BelemRestaurantes.UnitTests/CuiaDiscovery360Tests.cs): 10 novos testes cobrindo SSRF, DNS multi-homed, tiers de mídia, IBGE e rastreabilidade.

---

## 3. Vulnerabilidades Corrigidas (Fase 1 e Fase 9)

| Vulnerabilidade / Risco | Mecanismo de Correção Implementado |
| :--- | :--- |
| **SSRF via DNS Multi-Homed** | `addresses.Any(IsDisallowedIp)` aborta a requisição caso qualquer IP retornado seja reservado/privado, anulando técnicas de DNS rebinding. |
| **SSRF via HTTP Redirects** | `AllowAutoRedirect = false`; inspeção manual dos headers `Location`, bloqueando saltos para esquemas não-HTTP, portas não autorizadas ou hosts internos. |
| **Exaustão de Recursos / Respostas Gigantes** | Limite rígido de 2 MB no `HttpContent.ReadAsStreamAsync()` com corte imediato ao exceder o buffer. |
| **Vazamento de Stacktrace no Painel** | Mascaramento de erros com mensagens seguras e log estruturado com `ILogger`. |
| **Prompt Injection via Páginas Externas** | Todo dado extraído de HTML é encapsulado como dado não confiável (`disclaimer`), impedindo que comandos arbitrários sejam executados pela Cuia IA. |
| **Sobrescrita Indevida de Comerciantes** | Prioridade Tier 1 para dados fornecidos por comerciantes verificados (`restaurant_owners`). Divergências viram pendências de moderação (`ModerationReason.DataConflict`). |

---

## 4. Indicadores de Cobertura Estadual Real (Fase 2)

O sistema utiliza códigos oficiais do IBGE para municípios paraenses e mantém os seguintes indicadores em tempo real:
1. **Total Descoberto por Município** (`COUNT(r.id)`)
2. **Aprovados no Catálogo** (`status = 'published'`)
3. **Pendentes na Moderação** (`status = 'pending'`)
4. **Sem Fotos Cadastradas** (`photo_url IS NULL OR photo_url = ''`)
5. **Dados Desatualizados** (`updated_at < NOW() - 90 dias`)
6. **Data e Hora da Última Varredura por Região**
7. **Status de Cobertura** (`unscanned`, `in_progress`, `covered`, `stale`)

---

## 5. Resultados de Compilação & Testes

* **Build (Debug):** 0 Avisos, 0 Erros (7 projetos compilados).
* **Build (Release):** 0 Avisos, 0 Erros.
* **Testes Unitários:** 124 testes executados, 124 aprovados (100% de sucesso), 0 ignorados.

---

## 6. Instruções de Publicação (VPS Windows + IIS)

1. **Compilar e Publicar via Visual Studio Community 2026:**
   * Abrir a solution `BelemRestaurantes.sln`.
   * Publicar o projeto `BelemRestaurantes.Web` no perfil de publicação do IIS de homologação/produção.
2. **Execução das Migrações:**
   * Ao inicializar a aplicação Web ou o Worker, o `MigrationRunner` embutido detectará a migração `013_cuia_discovery_360.sql` e a aplicará automaticamente com lock atômico em `belrestaurante`.
3. **Instalação / Reinicialização do Windows Service:**
   ```powershell
   sc.exe stop BelemRestauranteCuiaDiscovery
   dotnet publish src\BelemRestaurantes.Worker\BelemRestaurantes.Worker.csproj -c Release -o "C:\Servicos\BelemRestaurantesWorker"
   sc.exe start BelemRestauranteCuiaDiscovery
   ```
