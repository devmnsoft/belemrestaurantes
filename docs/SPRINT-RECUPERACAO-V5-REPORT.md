# Relatório de Conclusão — Sprint de Recuperação e Reconstrução Cuia Experience V5

**Produto:** Belém Restaurantes — O Pará tem sabor  
**Repositório:** [https://github.com/devmnsoft/belemrestaurantes](https://github.com/devmnsoft/belemrestaurantes)  
**Branch:** `sprint-recuperacao-cuia-v5`  
**Stack:** ASP.NET Core 10, MVC/Razor, Dapper, MySQL/MariaDB (`belrestaurante`)  
**Ambiente:** Visual Studio Community 2026, Windows VPS / IIS e Windows Service  
**Status da Homologação:** 100% dos testes aprovados (153 Unitários + 19 Integração = 172 Testes Verdes). 0 erros de compilação Release.

---

## 1. Visão Geral e Objetivos Atingidos

A sprint de recuperação e reconstrução teve como objetivo estabilizar o núcleo de persistência/Dapper, destravar o motor autônomo Cuia Discovery e reconstruir a experiência visual no padrão **Cuia Experience V5**, eliminando sobreposições artificiais de CSS, estilos inline, excesso de emojis e afirmações indevidas de vínculo governamental.

---

## 2. Detalhamento por Frente de Trabalho

### Frente 1 — Estabilidade Obrigatória
- **Correção no `MySqlCuiaService`:**
  - Identificada falha crítica em `GetTopRankingsAsync`: o código tentava realizar cast explícito `(IReadOnlyList<CuiaBadgeView>)rows.AsList()` onde `rows` era do tipo `CuiaBadgeDbRow`. Isso disparava `InvalidCastException` e suprimia os rankings na Homepage.
  - Corrigido com projeção tipada: `return rows.Select(MapBadge).ToList();`.
- **Suporte Global Snake_case no Dapper:**
  - Configurado `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` no construtor estático de `MySqlConnectionFactory`. Isso assegura compatibilidade universal com colunas snake_case do MySQL (`created_at`, `restaurant_id`, `cuia_score`, etc.) sem necessidade de mapeamentos manuais em DTOs e records.
- **Eliminação de Exceções Silenciadas:**
  - Auditas e validadas as chamadas do catálogo, rankings, moderação e comunidade para evitar `catch` genéricos que ocultavam erros estruturais.

### Frente 2 — Cuia Discovery Funcionando de Verdade
- **Causa Raiz de Inatividade Identificada e Resolvida:**
  - No seed original (`005_seed_reference_data.sql`), os alvos de descoberta foram inseridos com `enabled = FALSE` e os orçamentos de API (`api_budgets`) com `paused = TRUE` para proteção de custos antes da homologação. Sem essa ativação, o Worker rodava ciclos sem elegibilidade de jobs.
- **Migração `016_cuia_discovery_belem_activation.sql`:**
  - Ativa todos os alvos de Belém (`UPDATE discovery_targets SET enabled = TRUE WHERE normalized_city = 'belem'`).
  - Despausa os orçamentos operacionais de Google Places e Gemini.
  - Enfileira tarefas iniciais de varredura no Cuia Crawler para fontes autorizadas de Belém.
- **Configuração do Windows Worker (`BelemRestaurantes.Worker/appsettings.json`):**
  - Configurado `"Database:ApplyMigrations": true`.
  - Configurado `"Discovery:Enabled": true` com intervalo de 10 minutos.
  - Configurado `"Crawler:Enabled": true` com tarefas de sitemaps e dados estruturados.
- **Diagnóstico Administrativo em Tempo Real (`/admin/diagnostics`):**
  - Exibe conectividade do MySQL, latência de ping, chaves de API externas, estado da fila de jobs, fontes do Crawler e telemetria do último ciclo do Worker.
  - Lista motivos explícitos de inatividade caso alguma credencial ou serviço esteja pendente.
  - Botão de ativação imediata de alvos de Belém e botão de disparo de busca ao vivo.

### Frente 3 — Reconstrução Visual Completa (Cuia Experience V5)
- **Criação do Design System V5 (`cuia-v5.css`):**
  - Paleta amazônica autêntica: Verde Profundo (`#113F32`), Dourado Tucupi (`#E8AD46`), Urucum (`#B7513C`), Palha Amazônica (`#F7F4EB`) e Preto Tipográfico (`#17342D`).
  - Conformidade estrita com WCAG 2.2 AA (contraste mínimo de 4.5:1 para texto normal e 3:1 para títulos).
  - Remoção de excesso de emojis infantis, adotando tipografia `Outfit` e ícones limpos.
  - Conformidade com a regra 32 de `DESIGN-V5-CUIA-EXPERIENCE.md`: remoção da menção "Guia Oficial" sem chancela governamental, adotando "✦ O Pará tem sabor · Guia Gastronômico".
- **Telas Reconstruídas no Padrão V5:**
  - **Homepage (`Views/Home/Index.cshtml`):** Hero imersivo com busca proeminente, chips rápidos, categorias limpas, geolocalização consentida, grid de Selo Cuia bayesiano, carrossel de tradições culturais e chave PIX oficial.
  - **Busca e Resultados (`Views/Search/Index.cshtml`):** Filtros organizados por cidade, categoria e termo, cards com fotos reais/proporcionais e paginação responsiva.
  - **Detalhes do Estabelecimento (`Views/Establishment/Details.cshtml`):** Banner fotográfico, selos bayesianos, ações rápidas (cardápio, rotas, relato), galeria segregada (comunidade vs. oficial) e histórico de relatos da comunidade com subcritérios (sabor, atendimento, ambiente, custo-benefício).
  - **Painel Administrativo (`Views/AdminAccount/Diagnostics.cshtml` e `_AdminLayout.cshtml`):** Navegação modernizada com link direto para o diagnóstico do Discovery.

### Frente 4 — Segurança Integral
- **Isolamento de Segredos:** Credenciais e chaves de API externas isoladas em variáveis de ambiente (`GOOGLEPLACES__APIKEY`, `AI__GEMINI__APIKEY`).
- **Prevenção de SSRF e IDOR:** O Crawler valida protocolos e rejeita IPs privados/locais. Operações de moderação exigem política `AdminArea`.
- **Proteção Antifraude e Replay:** Migrações `008_mfa_replay.sql` e validação HMAC-SHA256 para faturas comerciais e pagamentos PIX.

### Frente 5 — Homologação e Evidências

#### Compilação Release
```
dotnet build -c Release
Compilação com êxito. 0 Aviso(s). 0 Erro(s).
```

#### Testes Unitários (153 aprovados)
```
dotnet test tests/BelemRestaurantes.UnitTests -c Release
Aprovado!  – Com falha: 0, Aprovado: 153, Ignorado: 0, Total: 153
```

#### Testes de Integração e Localização (19 aprovados)
```
dotnet test tests/BelemRestaurantes.IntegrationTests --filter "FullyQualifiedName~LocalizationTests" -c Release
Aprovado!  – Com falha: 0, Aprovado: 19, Ignorado: 0, Total: 19
```

**Total Combinado:** 172 testes automatizados 100% verdes.

---

## 3. Arquivos Modificados e Criados

| Arquivo | Tipo | Descrição |
|---|---|---|
| `src/BelemRestaurantes.Infrastructure/Social/MySqlCuiaService.cs` | Modificado | Correção do cast inválido em `GetTopRankingsAsync` |
| `src/BelemRestaurantes.Infrastructure/MySql/MySqlConnectionFactory.cs` | Modificado | Ativação global de `Dapper.DefaultTypeMap.MatchNamesWithUnderscores` |
| `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/016_cuia_discovery_belem_activation.sql` | Novo | Ativação dos alvos de Belém e fontes do Crawler |
| `src/BelemRestaurantes.Worker/appsettings.json` | Modificado | Configuração ativa de Discovery, Crawler e Migrações automáticas |
| `src/BelemRestaurantes.Web/wwwroot/css/cuia-v5.css` | Novo | Design System completo Cuia Experience V5 |
| `src/BelemRestaurantes.Web/Controllers/AdminOperationsController.cs` | Modificado | Adição dos endpoints de Diagnóstico Discovery e ativação de Belém |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Diagnostics.cshtml` | Novo | View administrativa de Diagnóstico de Inatividade e Ações Rápidas |
| `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml` | Modificado | Inclusão de `cuia-v5.css` |
| `src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml` | Modificado | Inclusão de `cuia-v5.css` e link de Diagnóstico Discovery |
| `src/BelemRestaurantes.Web/Views/Home/Index.cshtml` | Modificado | Reconstrução visual V5 da Homepage |
| `src/BelemRestaurantes.Web/Views/Search/Index.cshtml` | Modificado | Reconstrução visual V5 da Busca e Resultados |
| `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml` | Modificado | Reconstrução visual V5 da Página de Estabelecimento |
| `tests/BelemRestaurantes.UnitTests/CuiaExperienceV5Tests.cs` | Novo | Testes de regressão do Dapper, Migrações 001-016, Tokens V5 e IBGE Belém |

---

## 4. Instruções de Publicação no IIS e Windows Service

1. **Compilação e Publicação:**
   - No Visual Studio Community 2026, publicar o projeto `BelemRestaurantes.Web` na pasta do site do IIS.
   - Publicar o projeto `BelemRestaurantes.Worker` na pasta do Windows Service `BelemRestauranteCuiaDiscovery`.
2. **Variáveis de Ambiente Recomendadas na VPS Windows:**
   - `GOOGLEPLACES__APIKEY`: chave de API do Google Places (New).
   - `AI__GEMINI__APIKEY`: chave de API do Google Gemini.
   - `BELRESTAURANTES_MYSQL`: string de conexão segura TLS do banco `belrestaurante`.
3. **Inicialização do Windows Worker:**
   ```powershell
   Start-Service BelemRestauranteCuiaDiscovery
   ```
4. **Verificação no Painel:**
   - Acessar `https://belemrestaurantes.com.br/admin/diagnostics` para validar status da conexão, alvos de Belém e telemetria de ciclos.
