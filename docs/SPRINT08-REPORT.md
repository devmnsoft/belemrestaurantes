# Relatório de Conclusão da Sprint 08 — Belém Restaurantes
**Cuia Crawler: Pesquisa Autônoma na Internet, Descoberta Gastronômica e Enriquecimento Seguro do Catálogo**  
**Data:** 17 de Outubro de 2026  
**Tecnologia:** ASP.NET Core 10 (net10.0), C#, MVC/Razor, Dapper, MySQL `belrestaurante`, Windows Service  
**Empresa / Proprietário:** MNSOFT Soluções (CNPJ: 18.160.057/0001-13)  
**Ambiente Alvo:** Windows Server VPS + IIS 10 + Windows Service (`BelemRestauranteCuiaDiscovery`)  

---

## 1. Missão e Arquitetura do Cuia Crawler

A **Sprint 08** implementou o motor de **pesquisa e rastreamento autônomo** do Belém Restaurantes, reduzindo a dependência de APIs comerciais e expandindo a cobertura gastronômica por todo o estado do Pará (Região Metropolitana de Belém, Baixo Amazonas, Sudeste Paraense, Marajó e Tapajós).

```
[Fontes Públicas Autorizadas]
(Sitemaps XML, Feeds RSS, Schema.org)
           │
           ▼
[SafeHttpClient & SSRF Guard] ──(Bloqueia IPs privados, Loopback, Cloud Metadata)
           │
           ▼
   [RobotsTxtService] ──────────(Respeita Disallow e Crawl-delay)
           │
           ▼
    [CrawlerParser] ────────────(Extrai JSON-LD Restaurant, Geo, Contatos e Redes)
           │
           ▼
       [Cuia IA] ───────────────(Gemini / Groq / DeepSeek com defesa de Prompt Injection)
           │
           ▼
  [DeduplicationService] ───────(Compara nomes normalizados e municípios)
           │
           ├───[Sem duplicata] ───────► Inserção no catálogo (estado: pending)
           └───[Conflito/Incerteza] ──► Fila de Moderação Administrativa MNSOFT
```

---

## 2. Inventário de Funcionalidades Entregues

| Módulo / Funcionalidade | Estado Anterior | Estado Atual na Sprint 08 | Evidência / Arquivos |
| :--- | :--- | :--- | :--- |
| **Cuia Crawler Service** | Descoberta dependente apenas de Google Places API | Serviço autônomo com suporte a Sitemaps XML, RSS e Schema.org Restaurant | `src/BelemRestaurantes.Application/Services/CrawlerOrchestrator.cs`<br>`src/BelemRestaurantes.Worker/CrawlerHostedService.cs` |
| **Proteção Anti-SSRF e DNS Rebinding** | Cliente HTTP sem verificação de IP resolvido | `SafeHttpClient` com validação de IP no socket, proibindo RFC 1918, loopback e metadados de nuvem (169.254.169.254) | `src/BelemRestaurantes.Infrastructure/Crawler/SafeHttpClient.cs` |
| **Respeito a robots.txt** | Inexistente | `RobotsTxtService` com cache de 24h, respeito estrito a `Disallow` e `Crawl-delay` por domínio | `src/BelemRestaurantes.Infrastructure/Crawler/RobotsTxtService.cs` |
| **Parser Estruturado** | Inexistente | `CrawlerParser` extraindo Schema.org JSON-LD (`Restaurant`, `FoodEstablishment`), geolocalização e redes sociais | `src/BelemRestaurantes.Infrastructure/Crawler/CrawlerParser.cs` |
| **Fila e Telemetria MySQL** | Apenas discovery_jobs legado | Tabelas `crawler_sources`, `crawler_tasks` (com `FOR UPDATE SKIP LOCKED`) e `crawler_runs` | `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/012_cuia_crawler.sql` |
| **Painel de Controle MNSOFT** | Sem interface para crawler | Tela completa em `/admin/crawler` com indicadores reais, gestão de fontes, bloqueios e execuções | `src/BelemRestaurantes.Web/Controllers/AdminCrawlerController.cs`<br>`src/BelemRestaurantes.Web/Views/AdminAccount/Crawler.cshtml` |
| **Testes Unitários Automatizados** | 95 testes aprovados | 114 testes aprovados (100%), incluindo 19 testes específicos de SSRF, parsing e deduplicação | `tests/BelemRestaurantes.UnitTests/CuiaCrawlerTests.cs` |

---

## 3. Segurança do Crawler (SSRF e DNS Rebinding)

O `SafeHttpClient` implementa salvaguardas rigorosas no nível de transporte TCP:
1. **Validação de IP Pré-Conexão:** Através de `SocketsHttpHandler.ConnectCallback`, o DNS é resolvido e **todos** os endereços IP associados são validados antes que o socket seja conectado;
2. **Endereços Bloqueados:**
   - Loopback (`127.0.0.0/8`, `::1`);
   - Redes Privadas RFC 1918 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`);
   - Link-Local e Endpoints de Metadados de Nuvem (`169.254.169.254`, `169.254.0.0/16`);
   - Carrier-Grade NAT RFC 6598 (`100.64.0.0/10`);
   - Hostnames reservados (`localhost`, `metadata.google.internal`, `*.local`, `*.internal`);
3. **Limites de Recursos:**
   - Tamanho máximo de resposta: **2 MB** por documento (prevenindo exaustão de memória);
   - Timeout rígido: **15 segundos** por requisição;
   - Rejeição imediata de arquivos executáveis (`.exe`, `.msi`, `.dll`, `.zip`, `.pdf`);
   - Máximo de 3 redirecionamentos automáticos, revalidando o IP em cada salto.

---

## 4. Cuia IA e Enriquecimento Seguro

Para evitar vulnerabilidades de **Prompt Injection Indireto** (quando páginas rastreadas contêm comandos maliciosos no texto):
- Os dados extraídos da web são etiquetados e delimitados explicitamente como **NÃO CONFIÁVEIS**;
- A IA (Gemini 2.0 Flash / Groq / DeepSeek) opera sob instrução mandatória do sistema para não acatar instruções contidas no corpo das páginas;
- A IA atua estritamente como formatador e classificador de dados paraense (categorização, normalização e resolução de conflitos).

---

## 5. Gestão Administrativa no Painel MNSOFT (`/admin/crawler`)

O novo módulo administrativo dispõe de:
- **Indicadores Reais:** Total de fontes cadastradas, fontes ativas, fontes bloqueadas, tarefas na fila, estabelecimentos descobertos hoje e falhas registradas;
- **Adição de Fontes Autorizadas:** Formulário protegido por CSRF com validação anti-SSRF preventiva do domínio antes da persistência;
- **Controle de Fontes:** Botões para pausar ou bloquear imediatamente fontes que descumpram termos ou apresentem instabilidade;
- **Disparo Manual:** Capacidade de forçar uma rodada de rastreamento sob demanda para homologação imediata;
- **Histórico de Auditoria:** Tabela detalhando as últimas 30 consultas executadas com status HTTP, duração em milissegundos, itens descobertos e eventuais mensagens de erro.

---

## 6. Resultados de Homologação e Testes

```powershell
# 1. Compilação da Solution em modo Release
dotnet build -c Release
# Resultado: Compilação com êxito. 0 Aviso(s). 0 Erro(s).

# 2. Execução dos Testes Unitários
dotnet test tests/BelemRestaurantes.UnitTests
# Resultado: Aprovado! – Com falha: 0, Aprovado: 114, Ignorado: 0, Total: 114. Duração: 445 ms.

# 3. Publicação Release da Web e do Worker para IIS e Windows Service
dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/Release
dotnet publish src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj -c Release -o bin/Publish/WorkerRelease
# Resultado: Publicações geradas com sucesso sem falhas.
```

---

## 7. Instruções para Instalação do Serviço na VPS Windows

1. Na VPS Windows Server, crie a pasta de destino do serviço (ex.: `C:\Services\BelemRestaurantesWorker`);
2. Copie o conteúdo da pasta `bin/Publish/WorkerRelease/` para a pasta de destino;
3. Crie uma conta de serviço Windows sem privilégios administrativos (ex.: `svc_cuiacrawler`);
4. Registre o serviço via PowerShell com privilégios de Administrador:
   ```powershell
   sc.exe create BelemRestauranteCuiaDiscovery `
     binPath= "C:\Services\BelemRestaurantesWorker\BelemRestaurantes.Worker.exe" `
     start= auto `
     DisplayName= "Belém Restaurantes — Cuia Discovery & Crawler"
   ```
5. Defina a conta de execução sem privilégios de administrador:
   ```powershell
   sc.exe config BelemRestauranteCuiaDiscovery obj= ".\svc_cuiacrawler" password= "SuaSenhaSegura"
   ```
6. Conceda permissão de leitura e execução à conta na pasta do serviço e inicie o Windows Service:
   ```powershell
   Start-Service BelemRestauranteCuiaDiscovery
   ```
7. O serviço iniciará o ciclo autônomo a cada 10 minutos, registrando toda a atividade no MySQL `belrestaurante` e no Visualizador de Eventos do Windows.
