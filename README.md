# Belém Restaurantes — O Pará tem sabor

> **Sprint 25 — catálogo e leitura:** corrigida a consulta administrativa, com artes por categoria, controles para baixa visão, gestão de vídeos e lotes de 1.000 registros (teto configurável de 2.000). Consulte [funcionalidades, testes e limites da validação](docs/SPRINT25-PORTAL.md).

> **Sprint 19 — receitas e operação estadual:** 12 receitas editoriais publicadas, CMS com versões, aprovação/publicação em etapas, dois estabelecimentos reais publicados após revisão, descoberta configurada para 144 municípios, rotinas de validade e novo visual de receitas e visitas. Consulte [relatório, testes, acesso local e pendências de produção](docs/SPRINT19-REPORT.md). A expansão rumo a 100 mil continua sujeita a fontes, deduplicação e moderação.

> **Expansão estadual e novo design:** coleta em 21 atividades econômicas, importação em lotes com checkpoint no banco, teto de 100 mil novos registros por comando e painel `/admin/cobertura`. Novo explorador das seis regiões, busca por município e tipo de comida em PT/EN/ES. A meta de 100 mil não representa cadastros já obtidos: consulte [quantidades verificadas, execução e limites](docs/EXPANSAO-PARA-100K.md).

Plataforma gastronômica e comunitária do Pará desenvolvida em ASP.NET Core 10 (`net10.0`), C#, MVC/Razor, Dapper e MySQL `belrestaurante`.  
**Empresa / Proprietário:** MNSOFT Soluções em Tecnologia (CNPJ: `18.160.057/0001-13`)  
**E-mail Oficial:** `comercial@belemrestaurantes.com.br`  
**Ambiente:** VPS Windows + IIS 10; Publicação manual pelo Visual Studio Community 2026; Descoberta autônoma contínua via Windows Service `BelemRestauranteCuiaDiscovery`.

> **Sprints 16 e 17 — operação e visitas:** central de aprovação em lote, monitoramento do Worker, três contas reais de homologação com MFA e troca inicial, gestão de acessos e conteúdo editorial, Cuia Rotas, horários com procedência e administração de fontes/tarifas. Consulte [funcionalidades, acesso local, testes e pendências externas](docs/SPRINT16-17-REPORT.md). O planejamento não inventa linhas, tarifas ou funcionamento; integrações externas ainda não homologadas estão identificadas no relatório.

> **Sprint 15 — validação de 18/10/2026:** adicionados **5.266 cadastros reais de empresas do setor de alimentação**, totalizando **5.747 em 135 municípios do Pará**, pendentes de moderação. Nova identidade editorial “O Pará. À flor do paladar.”, arte amazônica original, explorador de municípios, busca e entrada em PT/EN/ES. Consulte [relatório, evidências e limites da fonte](docs/SPRINT15-REPORT.md). Histórico: [Sprint 14](docs/SPRINT14-REPORT.md), [Sprint 13](docs/SPRINT13-REPORT.md) e [Sprint 12](docs/SPRINT12-REPORT.md).


---

## 🌟 Principais Módulos da Plataforma (Sprints 09, 10 e 11)

1. **Design System Belém Restaurantes V4 (`~/css/design-v4.css`):**
   - Identidade amazônica com paleta autêntica: Verde Floresta (`#1e7a54`), Dourado Tucupi (`#e5a93b`), Vermelho Urucum (`#d94326`) e Creme (`#fdfbf7`).
   - Cards gastronômicos de alto impacto, badges de status operacional (`operational`, `temporarily_closed`, `permanently_closed`), e conformidade WCAG 2.2 AA.

2. **Cobertura Territorial Oficial dos 144 Municípios do Pará (IBGE):**
   - Todos os 144 municípios oficiais do Pará cadastrados com códigos IBGE canônicos de 7 dígitos e mesorregiões.
   - Distinção clara entre municípios e localidades/ilhas fluviais (Icoaraci, Ilha do Combu, Mosqueiro, Outeiro, Cotijuba, Alter do Chão, Algodoal, Joanes).
   - URLs limpas e canônicas: `/cidades` e `/localidade/{slug}`.

3. **Cuia Discovery Intelligence & Crawler Autônomo:**
   - Fila central com 8 tipos de tarefas: Descoberta, Atualização, Enriquecimento, Verificação de Fontes, Deduplicação, Validação de Fechamento, Revisão de Mídias e Resolução de Conflitos.
   - Execução cíclica em lotes a cada 10 minutos com telemetria em tempo real persistida no MySQL (`discovery_run_telemetry`).
   - Proteção rigorosa contra **SSRF e DNS Rebinding** (bloqueio de IPs privados, loopback e metadados de nuvem 169.254.169.254).

4. **Qualidade do Catálogo Gastronômico (Profile Completeness Score):**
   - Avaliação algorítmica de completude do perfil (0 a 100%) priorizando estabelecimentos sem foto verificada, sem endereço ou sem contato.
   - Proteção estrita aos dados fornecidos por comerciantes verificados (Tier 1), direcionando divergências para moderação com auditoria.

5. **Motor de Mídias e Fotografias Autorizadas por Tiers:**
   - *Tier 1:* Fotos do Proprietário Verificado.
   - *Tier 2:* Fotos da Comunidade Aprovada.
   - *Tier 3:* Mídia Externa Licenciada (com atribuição e sem scraping invasivo).
   - *Tier 4:* Ilustração Oficial Padrão do Belém Restaurantes.

6. **Monetização Real e Faturas PIX MNSOFT:**
   - Chave PIX oficial CNPJ MNSOFT: `18.160.057/0001-13`.
   - Emissão de faturas de planos parceiros com TxID único, cópia-e-cola e autenticação antifraude via HMAC-SHA256 (`auth_hash`).

7. **Painel Central de Operações 360 (`/admin/discovery360`):**
   - Telemetria de execuções do worker (tempo de resposta, ciclos, registros descobertos e atualizados).
   - Gestão de faturas comerciais e confirmação autenticada.
   - Indicadores territoriais de cobertura do IBGE por mesorregião e cidade polo.
   - Moderação e auditoria contínua de alterações de atributos.

---

## 🚀 Desenvolvimento e Manutenção

1. **Requisitos:** .NET 10.0 SDK, Visual Studio Community 2026, MySQL 8.0+.
2. **Compilar em Release:**
   ```powershell
   dotnet build BelemRestaurantes.sln -c Release
   ```
3. **Executar Testes Automatizados:**
   ```powershell
   # Testes Unitários
   dotnet test tests/BelemRestaurantes.UnitTests -c Release

   # Testes de Integração de Localização
   dotnet test tests/BelemRestaurantes.IntegrationTests --filter "FullyQualifiedName~LocalizationTests" -c Release
   ```
4. **Executar Migrações do MySQL:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --migrate
   ```
5. **Provisionar Primeiro Administrador:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --provision-admin
   ```
6. **Publicação para IIS e Windows Service (VPS Windows):**
   ```powershell
   # Publicação do Portal Web para IIS
   dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/WebRelease

   # Publicação do Worker permanente de Descoberta (Windows Service)
   dotnet publish src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj -c Release -o bin/Publish/WorkerRelease
   ```

---

## 📄 Relatórios Técnicos Oficiais

- [Relatório da Sprint 11 (Homologação Integral, Segurança Avançada e Qualidade)](docs/SPRINT11-REPORT.md)
- [Relatório da Sprint 10 (Operação Gastronômica 360 e Monetização Real)](docs/SPRINT10-REPORT.md)
- [Relatório da Sprint 09 (Cuia Discovery 360 e Cobertura Estadual)](docs/SPRINT09-REPORT.md)
- [Relatório da Sprint 08 (Cuia Crawler: Pesquisa Autônoma e Segurança SSRF)](docs/SPRINT08-REPORT.md)
- [Relatório da Sprint 06 (Redesign Completo e Jornada Gastronômica)](docs/SPRINT06-REPORT.md)
- [Relatório da Sprint 05 (Portal do Parceiro, Cardápio e Monetização)](docs/SPRINT05-REPORT.md)
- [Relatório da Sprint 04 (Comunidade, Fotos e Selo Cuia)](docs/SPRINT04-REPORT.md)
- [Relatório da Sprint 03 (Governança e Migração)](docs/SPRINT03-REPORT.md)
