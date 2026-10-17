# Belém Restaurantes — O Pará tem sabor

Plataforma gastronômica e comunitária do Pará desenvolvida em ASP.NET Core 10 (net10.0), C#, MVC/Razor, Dapper e MySQL `belrestaurante`.  
**Empresa / Proprietário:** MNSOFT Soluções (CNPJ: 18.160.057/0001-13)  
**Ambiente:** VPS Windows + IIS 10; Publicação manual pelo Visual Studio Community 2026; Descoberta autônoma via Windows Service.

---

## 🌟 Principais Módulos da Plataforma (Sprints 06 e 08)

1. **Design System Belém Restaurantes V3 (`~/css/design-v3.css`):**
   - Cores representativas: Verde Floresta, Tucupi Dourado, Urucum e Creme Amazônico.
   - Conformidade WCAG 2.2 AA (alto contraste, anéis de foco, suporte a leitores de tela).
2. **Nova Homepage Gastronômica:**
   - 10 seções gastronômicas ricas: Hero com autocomplete assíncrono, Categorias Regionais, Proximidade por Geolocalização Consentida, Selo Cuia Oficial, Sabores da Comunidade, Sabores do Pará, Explore o Interior, Mobilidade Urbana, Portal do Parceiro e Apoio PIX MNSOFT.
3. **Busca Gastronômica Inteligente (`/busca` e `/api/search/autocomplete`):**
   - Pesquisa no backend de nomes de restaurantes, categorias, bairros e pratos típicos com tolerância a acentos e debounce de 250ms.
4. **Página Individual do Estabelecimento (`/estabelecimento/{id}`):**
   - Galerias segregadas (comunidade, oficiais e ilustrativas), cardápio digital integrado, notas detalhadas e integração com rotas.
5. **Portal do Parceiro Comerciante (`/partner`):**
   - Reivindicação segura de estabelecimento, edição de cardápio digital, envio de fotografias e contratação de planos comerciais.
6. **Painel Administrativo MNSOFT (`/admin`):**
   - Layout dedicado com sidebar lateral fixa (`_AdminLayout.cshtml`), triagem visual de descobertas do worker, catálogo, moderação de fotos e relatos, e status em tempo real de 8 conectores e Cuia IA (Google Places, Gemini, Groq, DeepSeek, redes sociais).
7. **Cuia Crawler — Rastreamento Autônomo e Descoberta Web (`/admin/crawler`):**
   - Rastreamento autônomo e gentil via Sitemaps XML, feeds RSS e Schema.org (`Restaurant`, `FoodEstablishment`).
   - Proteção rigorosa contra **SSRF e DNS Rebinding** (bloqueio de IPs privados, loopback e metadados de nuvem 169.254.169.254).
   - Respeito integral a `robots.txt` e `Crawl-delay`.
   - Execução em background a cada 10 minutos via Windows Service com usuário restrito.

---

## 🚀 Desenvolvimento e Manutenção

1. **Requisitos:** .NET 10.0 SDK, Visual Studio 2026 (ou VS Code), MySQL 8.0+.
2. **Restaurar e Compilar:**
   ```powershell
   dotnet restore
   dotnet build BelemRestaurantes.sln
   ```
3. **Executar Migrações:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --migrate
   ```
4. **Provisionar Primeiro Administrador:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --provision-admin
   ```
   *Cadastre o TOTP no aplicativo autenticador no primeiro login.*

5. **Executar Testes Unitários:**
   ```powershell
   dotnet test tests/BelemRestaurantes.UnitTests
   ```
   *(114 testes unitários cobrindo regras de catálogo, IA, autenticação, moderação, planos comerciais, busca, SSRF guard e crawler parser).*

6. **Publicação para IIS e Windows Service (Windows VPS):**
   ```powershell
   # Publicação do Portal Web
   dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/Release

   # Publicação do Worker de Descoberta e Crawler
   dotnet publish src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj -c Release -o bin/Publish/WorkerRelease
   ```
   *Ou execute a publicação diretamente pelo Visual Studio 2026 utilizando o perfil FolderProfile.*

---

## 📄 Documentação e Sprints Anteriores

- [Relatório da Sprint 08 (Cuia Crawler: Pesquisa Autônoma e Segurança SSRF)](docs/SPRINT08-REPORT.md)
- [Relatório da Sprint 06 (Redesign Completo, Jornada Gastronômica e Homologação)](docs/SPRINT06-REPORT.md)
- [Relatório da Sprint 05 (Portal do Parceiro, Cardápio e Monetização)](docs/SPRINT05-REPORT.md)
- [Relatório da Sprint 04 (Comunidade, Fotos e Selo Cuia)](docs/SPRINT04-REPORT.md)
- [Relatório da Sprint 03 (Governança e Migração)](docs/SPRINT03-REPORT.md)
