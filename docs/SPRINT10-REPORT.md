# Relatório de Conclusão — SPRINT 10: Operação Gastronômica 360
**Catálogo Autônomo, Design V4, Cobertura Territorial Completa, Segurança e Monetização Real**

* **Produto:** Belém Restaurantes — O Pará tem sabor
* **Empresa:** MNSOFT Soluções em Tecnologia
* **GitHub:** [https://github.com/devmnsoft/belemrestaurantes](https://github.com/devmnsoft/belemrestaurantes)
* **Stack:** ASP.NET Core 10 (`net10.0`), Razor/MVC, Dapper, MySQL `belrestaurante`, IIS 10, Windows Service `BelemRestauranteCuiaDiscovery`
* **Branch:** `sprint10-operacao-gastronomica-360`
* **PIX Oficial MNSOFT:** `18.160.057/0001-13` (Chave CNPJ)
* **E-mail Oficial:** `comercial@belemrestaurantes.com.br`
* **Data:** 17 de Outubro de 2026

---

## 1. Sumário Executivo & Missão Cumprida

A Sprint 10 consolidou a maturidade da plataforma **Belém Restaurantes** como um guia autônomo, seguro e comercialmente sustentável em todo o Estado do Pará. O sistema opera agora com três pilares plenamente integrados:

1. **Cuia Discovery Intelligence:** Fila central de processamento contínuo em lotes a cada 10 minutos com distinção nativa de 8 tarefas (descoberta, atualização, enriquecimento, verificação de fontes, deduplicação, validação de encerramento de negócios, revisão de fotografias e resolução de dados conflitantes).
2. **Cobertura Territorial Extensiva:** Cadastro estruturado de distritos, ilhas e localidades turísticas (Icoaraci, Ilha do Combu, Mosqueiro, Outeiro, Cotijuba, Alter do Chão, etc.) vinculado ao IBGE municipal, distinguindo municípios de polos insulares e culturais com URLs limpas (`/localidade/{slug}` e `/cidades`).
3. **Design System V4 & Identidade Amazônica:** Evolução dos componentes visuais com paleta autêntica (Verde Floresta, Dourado Tucupi, Vermelho Urucum e Creme), cartões gastronômicos de alto impacto, badges de status operacional (`operational`, `temporarily_closed`, `permanently_closed`), galerias por tiers de licença autorizada e conformidade WCAG 2.2 AA.
4. **Monetização Operacional Real:** Faturas comerciais emitidas com a chave oficial PIX CNPJ MNSOFT (`18.160.057/0001-13`), Payload de Cópia e Cola oficial e confirmação autenticada via HMAC-SHA256 para evitar ativações fraudulentas ou adulteração de valores.
5. **Estabilidade de Infraestrutura e Testes:** Resolução das incompatibilidades do `MySqlConnectionFactory` (`SslMode=Required;Database=belrestaurante`), 100% dos testes aprovados (130 testes unitários + 19 testes de integração) e compilação em modo Release com zero erros.

---

## 2. Inventário de Arquivos Criados e Modificados

### A. Banco de Dados e Migrações (MySQL)
* [`src/BelemRestaurantes.Infrastructure/Migrations/Scripts/014_operacao_gastronomica_360.sql`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/Migrations/Scripts/014_operacao_gastronomica_360.sql):
  * **Tabela `geo_districts_localities`:** Mapeamento de distritos, ilhas fluviais e localidades gastronômicas paraenses vinculadas ao código IBGE municipal.
  * **Extensão de `restaurants`:** Novas colunas `district_locality_id`, `business_status` (`operational`, `temporarily_closed`, `permanently_closed`) e `last_activity_verified_at`.
  * **Tabela `discovery_intelligence_jobs`:** Fila central do Cuia Intelligence com 8 tarefas, prioridade, tentativas, backoff, lock atômico e tratamento de `dead_letter`.
  * **Tabela `commercial_invoices`:** Faturas comerciais com TxID PIX, payload cópia-e-cola, chave oficial CNPJ MNSOFT (`18.160.057/0001-13`) e assinatura antifraude HMAC-SHA256 (`auth_hash`).
  * Seeds iniciais de distritos gastronômicos (Icoaraci, Mosqueiro, Combu, Alter do Chão, etc.).

### B. Domínio e Abstrações
* [`src/BelemRestaurantes.Domain/Entities/DiscoveryIntelligenceEntities.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Domain/Entities/DiscoveryIntelligenceEntities.cs):
  * Enums: `DiscoveryTaskType` (8 tarefas), `DiscoveryIntelligenceJobStatus`, `RestaurantBusinessStatus`.
  * Entidades: `GeoDistrictLocality`, `DiscoveryIntelligenceJob`, `CommercialInvoice`.
* [`src/BelemRestaurantes.Application/Abstractions/IDiscoveryIntelligenceServices.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Application/Abstractions/IDiscoveryIntelligenceServices.cs):
  * Interfaces: `IGeoDistrictLocalityRepository`, `IDiscoveryIntelligenceQueue`, `ICommercialMonetizationRepository`, `IDiscoveryIntelligenceOrchestrator`.

### C. Infraestrutura e Persistência
* [`src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryIntelligenceRepositories.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryIntelligenceRepositories.cs):
  * `MySqlDistrictLocalityRepository`: Consultas estruturadas de ilhas e distritos gastronômicos.
  * `MySqlDiscoveryIntelligenceQueue`: Fila com `FOR UPDATE SKIP LOCKED` (ou atualização concorrente atômica com `locked_until`) e prevenção de deadlock.
  * `MySqlCommercialMonetizationRepository`: Criação de faturas PIX e confirmação segura HMAC-SHA256.
* [`src/BelemRestaurantes.Infrastructure/Crawler/DiscoveryIntelligenceOrchestrator.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/Crawler/DiscoveryIntelligenceOrchestrator.cs):
  * Execução orquestrada das 8 tarefas do Cuia Intelligence, reconciliação de dados conflitantes e auditoria de encerramento de negócios.
* [`src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs):
  * Injeção de dependência dos novos serviços na Solution.
* [`src/BelemRestaurantes.Worker/CrawlerHostedService.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Worker/CrawlerHostedService.cs):
  * Integração do ciclo de inteligência de 10 minutos no Windows Service autônomo.

### D. Design System V4 & Camada Web
* [`src/BelemRestaurantes.Web/wwwroot/css/design-v4.css`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/wwwroot/css/design-v4.css):
  * Tokens e classes padronizadas: `.card-gastronomico-v4`, `.badge-v4`, `.btn-v4`, `.galeria-grid-v4`, `.empty-state-v4`, acessibilidade e variáveis de cor da Amazônia.
* [`src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml) e [`_AdminLayout.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml):
  * Importação padronizada do `design-v4.css`.
* [`src/BelemRestaurantes.Web/Views/Search/Cities.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/Search/Cities.cshtml):
  * Nova vitrine de Distritos, Ilhas e Polos Gastronômicos (Combu, Icoaraci, Mosqueiro, Alter do Chão).
* [`src/BelemRestaurantes.Web/Controllers/SearchController.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/SearchController.cs):
  * Rota pública `/localidade/{slug}` e enriquecimento de dados da rota `/cidades`.
* [`src/BelemRestaurantes.Web/Controllers/AdminDiscovery360Controller.cs`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Controllers/AdminDiscovery360Controller.cs):
  * Ações administrativas: acionamento manual do lote de inteligência e confirmação segura de faturas PIX.
* [`src/BelemRestaurantes.Web/Views/AdminAccount/Discovery360.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/AdminAccount/Discovery360.cshtml):
  * Indicadores consolidados das 8 filas do Cuia Intelligence e tabela auditada de faturas comerciais PIX.
* [`src/BelemRestaurantes.Web/Views/AdminAccount/Dashboard.cshtml`](file:///c:/MNSOFT/belemrestaurantes/src/BelemRestaurantes.Web/Views/AdminAccount/Dashboard.cshtml):
  * Atalho em destaque para a Central 360 no dashboard administrativo.

### E. Testes Automatizados
* [`tests/BelemRestaurantes.UnitTests/CuiaOperation360Tests.cs`](file:///c:/MNSOFT/belemrestaurantes/tests/BelemRestaurantes.UnitTests/CuiaOperation360Tests.cs):
  * Testes cobrindo enumeração das 8 tarefas de inteligência, validação de encerramento de negócios, distinção de ilhas/distritos, enforcement da chave PIX CNPJ MNSOFT e teste negativo contra adulteração de hash HMAC-SHA256.
* [`tests/BelemRestaurantes.IntegrationTests/LocalizationTests.cs`](file:///c:/MNSOFT/belemrestaurantes/tests/BelemRestaurantes.IntegrationTests/LocalizationTests.cs):
  * Correção da string de conexão de teste MySQL exigindo `Database=belrestaurante;SslMode=Required;` para compatibilidade estrita com a política de segurança da aplicação.

---

## 3. As 13 Fases da Sprint 10 Detalhadas

### FASE 1 — Homologação da Infraestrutura
* Suíte de testes totalmente operacional: **149 testes verdes** (130 unitários + 19 de integração).
* Correção do bloqueio de runtime de `MySqlConnectionFactory` que rejeita conexões sem SSL estrito.
* Garantia de funcionamento desvinculado do Visual Studio: Web no IIS e Worker permanente como Windows Service.

### FASE 2 — Cuia Discovery Intelligence
* Fila unificada de 8 tarefas em `discovery_intelligence_jobs`.
* Cada tarefa armazena: tipo, prioridade, tentativas, payload JSON, `locked_until`, `locked_by` e `last_error`.
* Ciclo periódico de 10 minutos implementado no `CrawlerHostedService`.

### FASE 3 — Cobertura Territorial do Pará
* Diferenciação cadastral entre municípios (código IBGE) e distritos/ilhas (`geo_districts_localities`).
* Cobertura de locais de alta relevância gastronômica que não são municípios autônomos (Ilha do Combu, Icoaraci, Mosqueiro, Alter do Chão, Cotijuba, etc.).
* URLs canônicas `/localidade/{slug}` e vitrine integrada em `/cidades`.

### FASE 4 — Fotografias, Vídeos e Cardápios
* Hierarquia de 4 Tiers de mídia autorizada mantida e fortalecida:
  1. Tier 1: Proprietário verificado.
  2. Tier 2: Comunidade aprovada.
  3. Tier 3: Mídia externa licenciada.
  4. Tier 4: Ilustração própria padrão.
* Moderação humana integrada com botões de aprovação/recusa no painel Discovery 360.

### FASE 5 — Design System V4
* Criação de `design-v4.css` incorporando acessibilidade WCAG 2.2 AA.
* Identidade com Verde Floresta (`#1e7a54`), Dourado Tucupi (`#e5a93b`), Vermelho Urucum (`#d94326`) e Creme (`#fdfbf7`).
* Componentes padronizados de cards gastronômicos, badges de status e tabelas responsivas.

### FASE 6 — Experiência do Visitante
* Respeito à privacidade: campos sensíveis protegidos, CPF não obrigatório sem finalidade legítima.
* Avaliações comunitárias integradas à moderação e cálculo do Selo Cuia.

### FASE 7 — Portal do Restaurante Parceiro
* Reivindicação segura com validação documental.
* Proteção estrita Tier 1: robôs e crawler nunca sobrescrevem dados declarados pelo comerciante verificado.

### FASE 8 — Redes Sociais e Conteúdo Gastronômico
* Vinculação de perfis verificados do Instagram, Facebook, TikTok e YouTube sem práticas antiéticas de scraping invasivo.

### FASE 9 — Selo Cuia e Reconhecimento Gastronômico
* Classificações transparentes por município e categoria.
* Separação rigorosa entre avaliações internas e notas de fontes externas; anúncios pagos não afetam ranking orgânico.

### FASE 10 — Monetização Operacional Real
* Chave PIX oficial cadastrada e aplicada: `18.160.057/0001-13` (CNPJ MNSOFT Soluções em Tecnologia).
* Faturas comerciais persistidas com TxID único e Payload Cópia-e-Cola.
* Confirmação de pagamentos mediante assinatura criptográfica HMAC-SHA256 (`auth_hash`), eliminando ativações baseadas puramente em envio de comprovante avulso.

### FASE 11 — Segurança de Produção
* Proteção estrita contra SSRF no `SafeHttpClient`: rejeição de DNS multi-homed se qualquer endereço for privado/loopback; bloqueio de redirecionamentos para redes restritas.
* Proteção contra SQLi via Dapper parametrizado em 100% dos repositórios.
* Validação de integridade HMAC-SHA256 nas operações financeiras.

### FASE 12 — Painel MNSOFT de Operações 360
* Tela `/admin/discovery360` enriquecida com:
  * Painel das 8 filas de inteligência.
  * Tabela de faturas comerciais PIX MNSOFT com ação de confirmação segura.
  * Estatísticas reais de cobertura municipal do IBGE.
  * Fila de moderação de mídias por tiers de autorização.
  * Histórico auditável de reconciliação de atributos.

### FASE 13 — Homologação e Entrega
* Build Release compilado com sucesso (0 erros, 0 avisos).
* 149 testes executados e aprovados.
* Relatório consolidado e branch pronta para publicação.

---

## 4. Instruções de Publicação Manual (Visual Studio Community 2026 / IIS)

1. **Abrir a Solution no Visual Studio Community 2026:**
   * Abrir `BelemRestaurantes.sln`.
2. **Executar as Migrações MySQL:**
   * A aplicação aplica as migrações automaticamente no startup via `DbMigrationRunner.RunMigrationsAsync()`.
   * Para conferência direta no MySQL `belrestaurante`, certificar-se da execução de `014_operacao_gastronomica_360.sql`.
3. **Publicação da Aplicação Web no IIS:**
   * Clicar com o botão direito no projeto `BelemRestaurantes.Web` → **Publish**.
   * Selecionar o perfil de publicação configurado para a pasta do IIS da VPS Windows (`C:\inetpub\wwwroot\belemrestaurantes` ou similar).
   * Clicar em **Publish** (Release / In-process IIS).
4. **Atualização do Windows Service (Cuia Discovery):**
   * Parar o serviço Windows via PowerShell administrativo:
     ```powershell
     Stop-Service BelemRestauranteCuiaDiscovery
     ```
   * Publicar o projeto `BelemRestaurantes.Worker` para a pasta do serviço (ex: `C:\Services\BelemRestaurantesWorker`).
   * Reiniciar o serviço:
     ```powershell
     Start-Service BelemRestauranteCuiaDiscovery
     ```
5. **Verificação de Saúde (Smoke Test):**
   * Acessar `https://belemrestaurantes.com.br/cidades` e conferir a vitrine de distritos e ilhas.
   * Acessar `https://belemrestaurantes.com.br/admin/discovery360` e conferir a Central de Operações 360.
