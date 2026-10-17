# Relatório de Conclusão da Sprint 06 — Belém Restaurantes
**Redesign Completo, Jornada Gastronômica, Design System V3 e Homologação Funcional**  
**Data:** 17 de Outubro de 2026  
**Tecnologia:** ASP.NET Core 10 (net10.0), C#, MVC/Razor, Dapper, MySQL `belrestaurante`, JavaScript puro e CSS Nativo  
**Empresa / Proprietário:** MNSOFT Soluções (CNPJ: 18.160.057/0001-13)  
**Ambiente Alvo:** Windows Server VPS + IIS 10 + Visual Studio Community 2026  

---

## 1. Inventário Real de Funcionalidades Entregues

| Módulo / Funcionalidade | Estado Anterior | Estado Atual na Sprint 06 | Evidência / Arquivos |
| :--- | :--- | :--- | :--- |
| **Design System V3** | Arquivo `design-v2.css` com classes utilitárias dispersas | Design System unificado, consistente, paleta paraense autêntica, suporte WCAG 2.2 AA | `src/BelemRestaurantes.Web/wwwroot/css/design-v3.css` |
| **Homepage Gastronômica** | Lista de cards estática e simples | Nova homepage rica com 10 seções gastronômicas orientadas à conversão e comunidade | `src/BelemRestaurantes.Web/Views/Home/Index.cshtml`, `HomeController.cs` |
| **Busca Inteligente & Autocomplete** | Autocomplete apenas no frontend via JS sobre dados já carregados | Motor backend com Dapper, normalização de termos, tolerância a acentos e busca em pratos | `ISearchService.cs`, `MySqlSearchService.cs`, `SearchController.cs`, `Search/Index.cshtml` |
| **Página Individual do Restaurante** | Layout monótono sem segregação de fotos | Experiência premium com galeria segregada (comunidade/oficiais/ilustrativas), cardápio e rotas | `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml` |
| **Layout Administrativo MNSOFT** | Layout padrão sem menu lateral ou isolamento | Novo layout `_AdminLayout.cshtml` com sidebar fixa, 13 módulos e proteção de segurança | `src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml` |
| **Descoberta Automática (UI)** | Link apontando para JSON cru de API (`/api/admin/review/pending`) | Tela administrativa visual com aprovação/rejeição protegida por CSRF | `AdminOperationsController.cs`, `Views/AdminAccount/Discovery.cshtml` |
| **Catálogo Administrativo** | Inexistente na interface web | Painel com filtros de busca, cidades, notas Cuia e links diretos | `Views/AdminAccount/Restaurants.cshtml` |
| **Cuia IA e Conectores** | Sem tela de status, risco de vazamento de credenciais | Página dedicada de monitoramento de 8 provedores (Gemini, Groq, DeepSeek, Google Places, redes) | `Views/AdminAccount/Connectors.cshtml` |
| **Monetização & Apoio PIX** | Parcialmente disperso | Gestão de planos comerciais parceiros, pedidos e chave PIX institucional MNSOFT | `Views/AdminAccount/Monetization.cshtml` |
| **Homologação e Testes** | 91 testes unitários | 95 testes unitários aprovados (100%), build limpo (0 warnings, 0 erros) e Release verificado | `tests/BelemRestaurantes.UnitTests/SearchAndAdminTests.cs` |

---

## 2. Design System Belém Restaurantes V3

### 2.1 Identidade Visual e Paleta Cromática
- **Verde Floresta:** `#0d3826` / `#165338` (fundos institucionais, botões primários, navegação).
- **Tucupi Dourado:** `#e5a93b` / `#d49424` (destaques, Selo Cuia, estrelas de avaliação, badges).
- **Urucum:** `#c84b31` (alertas, rejeições, preços promocionais e destaques quentes).
- **Creme Amazônico:** `#fbf9f4` / `#f4f0e6` (superfície de leitura, cartões e contraste acolhedor).

### 2.2 Componentes Reutilizáveis
- `.hero-v3`: Banner de impacto com autocomplete integrado e seletor rápido.
- `.card-restaurant-v3`: Cards elevados com proporção 4:3, badges contextuais e botões de ação.
- `.admin-shell-v3` & `.admin-sidebar-v3`: Layout corporativo de 2 colunas para painel administrativo.
- `.table-v3`: Tabelas responsivas com cabeçalhos fixos e tipografia legível.
- `.tagline-badge` & `.badge-tag-v3`: Pílulas semânticas de categoria e status.
- `.star-rating-v3`: Visualizador de notas e critérios detalhados.

---

## 3. Busca Gastronômica Inteligente

### 3.1 Arquitetura da Busca
A busca foi transferida para o servidor através da abstração `ISearchService`:
1. **Autocomplete (`/api/search/autocomplete?q={termo}`)**:
   - Debounce no cliente (250ms);
   - Busca federada simultânea em `restaurants` (nome, categoria, bairro) e `menu_items` (nome de pratos paraenses, ingredientes);
   - Retorno estruturado com tipo (`restaurant`, `dish`, `category`), URL de destino e destaque visual.
2. **Página de Busca Avançada (`/busca`)**:
   - Filtros combinados de Termo, Cidade, Categoria e Bairro;
   - Paginação limpa com totalizador de resultados reais;
   - Ordenação com prioridade ao Selo Cuia.

---

## 4. Nova Homepage e Página do Restaurante

### 4.1 As 10 Seções da Homepage
1. **Hero Gastronômico:** Proposta de valor, busca preditiva e ações rápidas.
2. **Categorias Regionais:** Restaurantes, Tacacá, Açaí, Peixarias, Pizzarias, Cafeterias, Sorveterias.
3. **Descubra Perto de Você:** Estabelecimentos por proximidade com geolocalização **estritamente consentida** ou seleção manual de município.
4. **Selo Cuia:** Destaque dos melhores classificados com base na pontuação bayesiana real.
5. **Sabores da Comunidade:** Feed de experiências aprovadas de visitantes, com relatos e notas.
6. **Sabores do Pará:** Destaque dos pratos típicos (Maniçoba, Pato no Tucupi, Vatapá Paraense, Filhote na Brasa).
7. **Explore o Interior:** Navegação por municípios polos (Santarém, Marabá, Castanhal, Soure, etc.).
8. **Mobilidade Urbana:** Guias de como chegar com transporte público e integração com mapas.
9. **Portal do Parceiro:** Convite transparente para comerciantes reivindicarem suas páginas.
10. **Apoie o Projeto:** Sustentabilidade através do PIX oficial MNSOFT (`18.160.057/0001-13`).

---

## 5. Cuia IA e Governança de Conectores

Conforme critério da Sprint 06, nenhum conector é anunciado como ativo sem validação comprovada no banco:
- **Google Places API:** Monitoramento de chamadas do worker e registro de observações.
- **Google Gemini (gemini-2.0-flash):** Cadeia primária de IA; contabilidade de custo em USD via `ICostLedger`.
- **Groq (llama-3.3-70b-versatile):** Fallback ultrarrápido; métricas de chamadas e falhas.
- **DeepSeek (deepseek-chat):** Raciocínio avançado de 3º nível; telemetria em tempo real.
- **Instagram / Facebook / TikTok / YouTube:** Validação semântica e saneamento rigoroso de URLs.
- **Isolamento de Segredos:** Nenhuma chave, credencial ou token de API é retornado à interface ou ao navegador.

---

## 6. Resultados de Homologação e Testes

- **Compilação Solution:** 0 Erros, 0 Avisos (`dotnet build` com TreatWarningsAsErrors).
- **Testes Unitários:** 95 testes executados, 95 aprovados (100% de sucesso).
- **Publicação Release:** Testada com `dotnet publish -c Release -o bin/Publish/Release` (Sucesso absoluto).
- **Acessibilidade:** Suporte a WCAG 2.2 AA (contraste superior a 4.5:1, foco visível com `:focus-visible`, atributos ARIA e tags semânticas).
- **Segurança:** Todas as ações administrativas usam `[ValidateAntiForgeryToken]`, autorização segregada via cookies isolados de administração, sem exposição de IDs internos ou vetores IDOR.

---

## 7. Instruções para Publicação pelo Visual Studio Community 2026

1. Abra a Solution `BelemRestaurantes.sln` no **Visual Studio Community 2026**.
2. Selecione a configuração **Release** e plataforma **Any CPU**.
3. Clique com o botão direito no projeto `BelemRestaurantes.Web` e selecione **Publish**.
4. Utilize o perfil **FolderProfile** configurado para apontar para a pasta do IIS na VPS Windows (ou publique em pasta e copie via RDP/FTP seguro).
5. No IIS Manager:
   - Certifique-se de que o AppPool está configurado com `.NET CLR version: No Managed Code` (Core Module v2);
   - Verifique que as variáveis de ambiente `ConnectionStrings__MySql`, `GooglePlaces__ApiKey`, `Ai__Gemini__ApiKey` estão cadastradas nas variáveis de ambiente do sistema ou no `web.config` criptografado;
   - Reinicie o Application Pool do Belém Restaurantes.
