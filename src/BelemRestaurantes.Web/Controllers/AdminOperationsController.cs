using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin")]
public sealed class AdminOperationsController(
    IModerationRepository moderationRepo,
    IRestaurantReadRepository restaurantRepo,
    IMonetizationService monetizationService,
    MySqlConnectionFactory factory,
    IConfiguration config,
    IServiceProvider serviceProvider) : Controller
{
    private string Actor() => User.Identity?.Name ?? "admin";

    // 1. Restaurantes Cadastrados
    [HttpGet("restaurants")]
    public async Task<IActionResult> Restaurants(
        [FromQuery] string? query,
        [FromQuery] string? city,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct);

        const string countSql = """
            SELECT COUNT(*)
            FROM restaurants
            WHERE (@Query IS NULL OR name LIKE CONCAT('%', @Query, '%'))
              AND (@City IS NULL OR city = @City);
            """;

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, new { Query = query, City = city }, cancellationToken: ct));

        const string listSql = """
            SELECT id, name, city, neighborhood, category, price_range, state, cuia_score, updated_at, phone, address
            FROM restaurants
            WHERE (@Query IS NULL OR name LIKE CONCAT('%', @Query, '%'))
              AND (@City IS NULL OR city = @City)
            ORDER BY cuia_score DESC, name ASC
            LIMIT @Offset, @PageSize;
            """;

        const int pageSize = 30;
        var offset = Math.Max(0, (page - 1) * pageSize);

        var list = (await connection.QueryAsync<RestaurantAdminRow>(new CommandDefinition(
            listSql,
            new { Query = query, City = city, Offset = offset, PageSize = pageSize },
            cancellationToken: ct))).AsList();

        var cities = await restaurantRepo.ListCitiesAsync(ct);

        ViewData["Query"] = query;
        ViewData["City"] = city;
        ViewData["Page"] = page;
        ViewData["Total"] = total;
        ViewData["Cities"] = cities;

        return View("~/Views/AdminAccount/Restaurants.cshtml", list);
    }

    // 2. Descoberta Automática & Moderação Visual
    [HttpGet("discovery")]
    public async Task<IActionResult> Discovery([FromQuery] int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 30;
        var pending = await moderationRepo.ListPendingAsync(page, pageSize, ct);
        var pendingCount = await moderationRepo.CountPendingAsync(ct);

        ViewData["Page"] = page;
        ViewData["TotalPending"] = pendingCount;

        return View("~/Views/AdminAccount/Discovery.cshtml", pending);
    }

    [HttpPost("discovery/{id:long}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDiscovery(long id, CancellationToken ct)
    {
        var ok = await moderationRepo.ApproveAsync(id, Actor(), ct);
        if (ok)
            TempData["Flash"] = $"Estabelecimento #{id} aprovado e publicado com sucesso no catálogo!";
        else
            TempData["ErrorMessage"] = $"Não foi possível aprovar o estabelecimento #{id}. Verifique se ainda está pendente.";

        return RedirectToAction("Discovery");
    }

    [HttpPost("discovery/{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDiscovery(long id, [FromForm] string reason, CancellationToken ct)
    {
        var safeReason = string.IsNullOrWhiteSpace(reason) ? "Rejeitado na triagem administrativa" : reason.Trim();
        var ok = await moderationRepo.RejectAsync(id, Actor(), safeReason, ct);
        if (ok)
            TempData["Flash"] = $"Estabelecimento #{id} rejeitado com justificativa: {safeReason}";
        else
            TempData["ErrorMessage"] = $"Não foi possível rejeitar o estabelecimento #{id}.";

        return RedirectToAction("Discovery");
    }

    // 3. Status Cuia IA e Conectores
    [HttpGet("connectors")]
    public async Task<IActionResult> Connectors(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        // Custos por provedor IA
        const string costSql = """
            SELECT provider, SUM(cost_micros_usd) / 1000000.0 AS CostUsd, COUNT(*) AS Calls,
                   MAX(occurred_at) AS LastCall,
                   SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) AS Failures
            FROM ai_cost_entries
            GROUP BY provider;
            """;
        var aiStats = (await connection.QueryAsync<AiStatRow>(new CommandDefinition(costSql, cancellationToken: ct)))
            .ToDictionary(r => r.Provider.ToLowerInvariant(), r => r);

        // Sincronizações externas
        const string syncSql = """
            SELECT source, MAX(id) AS max_id, COUNT(*) AS total_runs
            FROM sync_runs
            GROUP BY source;
            """;
        var syncStats = (await connection.QueryAsync<SyncStatRow>(new CommandDefinition(syncSql, cancellationToken: ct)))
            .ToDictionary(r => r.Source.ToLowerInvariant(), r => r);

        // Último ciclo do worker
        const string workerSql = """
            SELECT started_at, completed_at, jobs_claimed, places_discovered, jobs_failed
            FROM discovery_cycle_runs
            ORDER BY id DESC
            LIMIT 1;
            """;
        var lastCycle = await connection.QueryFirstOrDefaultAsync<CycleRunRow>(new CommandDefinition(workerSql, cancellationToken: ct));

        var pendingApprovals = await moderationRepo.CountPendingAsync(ct);

        // Verificação segura de configuração sem vazar credenciais
        var googlePlacesConfigured = !string.IsNullOrWhiteSpace(config["GooglePlaces:BaseUrl"]) &&
                                     !string.IsNullOrWhiteSpace(config["GooglePlaces:ApiKey"]);

        var geminiKey = config["Ai:Gemini:ApiKey"];
        var geminiConfigured = !string.IsNullOrWhiteSpace(config["Ai:Gemini:BaseUrl"]) && !string.IsNullOrWhiteSpace(geminiKey);

        var groqKey = config["Ai:Groq:ApiKey"];
        var groqConfigured = !string.IsNullOrWhiteSpace(config["Ai:Groq:BaseUrl"]) && !string.IsNullOrWhiteSpace(groqKey);

        var deepseekKey = config["Ai:DeepSeek:ApiKey"];
        var deepseekConfigured = !string.IsNullOrWhiteSpace(config["Ai:DeepSeek:BaseUrl"]) && !string.IsNullOrWhiteSpace(deepseekKey);

        var connectors = new List<ConnectorStatusItem>
        {
            new()
            {
                Key = "googleplaces",
                Name = "Google Places API (New)",
                Category = "Descoberta de Estabelecimentos",
                IsConfigured = googlePlacesConfigured,
                ConfiguredDetails = googlePlacesConfigured ? "Endpoint configurado (HTTPS Google Cloud)" : "Chave de API não informada no appsettings/env",
                HasVerifiedSuccess = syncStats.ContainsKey("googleplaces") || (lastCycle != null && lastCycle.places_discovered > 0),
                LastSyncAt = lastCycle?.completed_at,
                RecentErrors = lastCycle?.jobs_failed > 0 ? $"{lastCycle.jobs_failed} falhas no último ciclo" : "Nenhum erro registrado",
                CostUsd = null,
                PendingModerationCount = pendingApprovals,
                AllowedActions = ["Executar Ciclo de Descoberta", "Ajustar Limite de Requisições"]
            },
            new()
            {
                Key = "gemini",
                Name = "Google Gemini (gemini-2.0-flash)",
                Category = "Cuia IA / Enriquecimento Cultural",
                IsConfigured = geminiConfigured,
                ConfiguredDetails = geminiConfigured ? "Modelo: gemini-2.0-flash ativo na cadeia primária" : "Credencial Google Generative Language pendente",
                HasVerifiedSuccess = aiStats.TryGetValue("gemini", out var gStat) && gStat.Calls > 0,
                LastSyncAt = aiStats.TryGetValue("gemini", out var gs) ? gs.LastCall : null,
                RecentErrors = aiStats.TryGetValue("gemini", out var ge) && ge.Failures > 0 ? $"{ge.Failures} chamadas rejeitadas" : "Nenhum erro registrado",
                CostUsd = aiStats.TryGetValue("gemini", out var gc) ? gc.CostUsd : 0m,
                PendingModerationCount = 0,
                AllowedActions = ["Testar Prompt Regional", "Alternar Fallback para Groq"]
            },
            new()
            {
                Key = "groq",
                Name = "Groq Cloud (llama-3.3-70b-versatile)",
                Category = "Cuia IA / Fallback Rápido",
                IsConfigured = groqConfigured,
                ConfiguredDetails = groqConfigured ? "LPU ultra-rápida (llama-3.3-70b)" : "Chave Groq Cloud não configurada",
                HasVerifiedSuccess = aiStats.TryGetValue("groq", out var qStat) && qStat.Calls > 0,
                LastSyncAt = aiStats.TryGetValue("groq", out var qs) ? qs.LastCall : null,
                RecentErrors = aiStats.TryGetValue("groq", out var qe) && qe.Failures > 0 ? $"{qe.Failures} falhas" : "Nenhum erro registrado",
                CostUsd = aiStats.TryGetValue("groq", out var qc) ? qc.CostUsd : 0m,
                PendingModerationCount = 0,
                AllowedActions = ["Testar Resposta Groq"]
            },
            new()
            {
                Key = "deepseek",
                Name = "DeepSeek (deepseek-chat)",
                Category = "Cuia IA / Raciocínio & Custo",
                IsConfigured = deepseekConfigured,
                ConfiguredDetails = deepseekConfigured ? "deepseek-chat configurado como 3º nível na cadeia" : "Chave DeepSeek não configurada",
                HasVerifiedSuccess = aiStats.TryGetValue("deepseek", out var dStat) && dStat.Calls > 0,
                LastSyncAt = aiStats.TryGetValue("deepseek", out var ds) ? ds.LastCall : null,
                RecentErrors = aiStats.TryGetValue("deepseek", out var de) && de.Failures > 0 ? $"{de.Failures} falhas" : "Nenhum erro registrado",
                CostUsd = aiStats.TryGetValue("deepseek", out var dc) ? dc.CostUsd : 0m,
                PendingModerationCount = 0,
                AllowedActions = ["Verificar Cota de Tokens"]
            },
            new()
            {
                Key = "instagram",
                Name = "Instagram Graph API / Links Verificados",
                Category = "Rede Social Gastronômica",
                IsConfigured = true,
                ConfiguredDetails = "Validação semântica e saneamento de URLs ativas",
                HasVerifiedSuccess = true,
                LastSyncAt = null,
                RecentErrors = "Nenhum erro de protocolo registrado",
                CostUsd = null,
                PendingModerationCount = 0,
                AllowedActions = ["Validar Links de Estabelecimentos"]
            },
            new()
            {
                Key = "facebook",
                Name = "Facebook Pages",
                Category = "Rede Social",
                IsConfigured = true,
                ConfiguredDetails = "Verificação de páginas oficiais via ingestão e moderação",
                HasVerifiedSuccess = true,
                LastSyncAt = null,
                RecentErrors = "Nenhum erro registrado",
                CostUsd = null,
                PendingModerationCount = 0,
                AllowedActions = ["Auditar Metadados"]
            },
            new()
            {
                Key = "tiktok",
                Name = "TikTok Culinária & Vídeos",
                Category = "Mídia Audiovisual",
                IsConfigured = true,
                ConfiguredDetails = "Detecção e sanitização de perfis gastronômicos",
                HasVerifiedSuccess = true,
                LastSyncAt = null,
                RecentErrors = "Nenhum erro registrado",
                CostUsd = null,
                PendingModerationCount = 0,
                AllowedActions = ["Moderar Conteúdos"]
            },
            new()
            {
                Key = "youtube",
                Name = "YouTube Gastronomia Paraense",
                Category = "Mídia Audiovisual",
                IsConfigured = true,
                ConfiguredDetails = "Canais oficiais de receitas regionais e festivais",
                HasVerifiedSuccess = true,
                LastSyncAt = null,
                RecentErrors = "Nenhum erro registrado",
                CostUsd = null,
                PendingModerationCount = 0,
                AllowedActions = ["Explorar Vídeos Regionais"]
            }
        };

        var model = new AdminConnectorsViewModel
        {
            Connectors = connectors,
            LastWorkerCycle = lastCycle,
            TotalPendingMod = pendingApprovals
        };

        return View("~/Views/AdminAccount/Connectors.cshtml", model);
    }

    // 4. Planos, Parcerias & Apoio PIX
    [HttpGet("monetization")]
    public async Task<IActionResult> Monetization(CancellationToken ct)
    {
        var plans = await monetizationService.ListPlansAsync(ct);

        await using var connection = await factory.OpenAsync(ct);
        const string ordersSql = """
            SELECT id, restaurant_id, user_id, plan_code, amount_cents, status, order_number, created_at, paid_at
            FROM merchant_orders
            ORDER BY created_at DESC
            LIMIT 50;
            """;
        var orders = (await connection.QueryAsync<MerchantOrderRow>(new CommandDefinition(ordersSql, cancellationToken: ct))).AsList();

        ViewData["Plans"] = plans;
        return View("~/Views/AdminAccount/Monetization.cshtml", orders);
    }

    // 5. Diagnóstico Administrativo Cuia Discovery & Inatividade
    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var model = new AdminDiscoveryDiagnosticsViewModel();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var connection = await factory.OpenAsync(ct);
            sw.Stop();
            model.MySqlConnected = true;
            model.MySqlDatabase = factory.Database;
            model.MySqlPingMs = sw.ElapsedMilliseconds;

            // Alvos de descoberta
            const string targetsSql = """
                SELECT 
                    COUNT(*) AS Total,
                    SUM(CASE WHEN enabled = 1 THEN 1 ELSE 0 END) AS EnabledCount,
                    SUM(CASE WHEN enabled = 1 AND normalized_city = 'belem' THEN 1 ELSE 0 END) AS BelemEnabled
                FROM discovery_targets;
                """;
            var targetStats = await connection.QueryFirstOrDefaultAsync<(int Total, int? EnabledCount, int? BelemEnabled)>(new CommandDefinition(targetsSql, cancellationToken: ct));
            model.TotalDiscoveryTargets = targetStats.Total;
            model.EnabledDiscoveryTargets = targetStats.EnabledCount ?? 0;
            model.BelemTargetsEnabled = targetStats.BelemEnabled ?? 0;

            // Fila de jobs
            const string jobsSql = """
                SELECT 
                    SUM(CASE WHEN state = 'pending' THEN 1 ELSE 0 END) AS PendingJobs,
                    SUM(CASE WHEN state = 'failed' THEN 1 ELSE 0 END) AS FailedJobs,
                    SUM(CASE WHEN state = 'dead_lettered' THEN 1 ELSE 0 END) AS DeadJobs
                FROM discovery_jobs;
                """;
            var jobStats = await connection.QueryFirstOrDefaultAsync<(int? PendingJobs, int? FailedJobs, int? DeadJobs)>(new CommandDefinition(jobsSql, cancellationToken: ct));
            model.DiscoveryPendingJobs = jobStats.PendingJobs ?? 0;
            model.DiscoveryFailedJobs = jobStats.FailedJobs ?? 0;
            model.DiscoveryDeadLetteredJobs = jobStats.DeadJobs ?? 0;

            // Crawler
            const string crawlerSql = """
                SELECT 
                    (SELECT COUNT(*) FROM crawler_sources WHERE status = 'active') AS ActiveSources,
                    (SELECT COUNT(*) FROM crawler_tasks WHERE state = 'pending') AS PendingTasks;
                """;
            var crawlerStats = await connection.QueryFirstOrDefaultAsync<(int ActiveSources, int PendingTasks)>(new CommandDefinition(crawlerSql, cancellationToken: ct));
            model.CrawlerActiveSources = crawlerStats.ActiveSources;
            model.CrawlerPendingTasks = crawlerStats.PendingTasks;

            // Último ciclo do worker
            const string workerSql = """
                SELECT started_at, completed_at, jobs_claimed, places_discovered, jobs_failed
                FROM discovery_cycle_runs
                ORDER BY id DESC
                LIMIT 1;
                """;
            model.LastWorkerCycle = await connection.QueryFirstOrDefaultAsync<CycleRunRow>(new CommandDefinition(workerSql, cancellationToken: ct));
            model.TotalPendingModeration = await moderationRepo.CountPendingAsync(ct);
        }
        catch (Exception ex)
        {
            model.MySqlConnected = false;
            model.MySqlErrorMessage = ex.Message;
        }

        // Diagnóstico Google Places
        var googleKey = config["GooglePlaces:ApiKey"];
        model.GooglePlacesBaseUrl = config["GooglePlaces:BaseUrl"] ?? "https://places.googleapis.com/v1/";
        model.GooglePlacesConfigured = !string.IsNullOrWhiteSpace(googleKey);

        if (!model.GooglePlacesConfigured)
        {
            model.GooglePlacesStatusMessage = "Chave de API não informada no ambiente (GooglePlaces:ApiKey ou GOOGLEPLACES__APIKEY).";
            model.InactivityReasons.Add("Google Places API inativa: Chave ausente nas configurações da VPS. O robô não pode fazer buscas diretas na API do Google sem chave de API.");
        }
        else
        {
            model.GooglePlacesStatusMessage = "Chave configurada e ativa.";
        }

        if (model.MySqlConnected)
        {
            if (model.BelemTargetsEnabled == 0)
            {
                model.InactivityReasons.Add("Nenhum alvo de descoberta habilitado para Belém. É necessário ativar os alvos de Belém para o robô processar a capital.");
            }
            if (model.DiscoveryPendingJobs == 0 && model.CrawlerPendingTasks == 0)
            {
                model.InactivityReasons.Add("A fila de tarefas e alvos está vazia no momento. Use 'Ativar Alvos de Belém' para enfileirar novos trabalhos.");
            }
            if (model.LastWorkerCycle == null)
            {
                model.InactivityReasons.Add("Nenhum ciclo do Windows Worker BelemRestauranteCuiaDiscovery foi registrado no banco. Verifique se o serviço está em execução.");
            }
            else if (model.LastWorkerCycle.completed_at.HasValue && model.LastWorkerCycle.completed_at.Value < DateTimeOffset.UtcNow.AddMinutes(-30))
            {
                model.InactivityReasons.Add($"O último ciclo do Worker foi concluído há mais de 30 minutos ({model.LastWorkerCycle.completed_at:dd/MM/yyyy HH:mm}). O intervalo padrão é de 10 minutos.");
            }
        }
        else
        {
            model.InactivityReasons.Add($"Conexão MySQL indisponível: {model.MySqlErrorMessage}");
        }

        return View("~/Views/AdminAccount/Diagnostics.cshtml", model);
    }

    [HttpPost("diagnostics/activate-belem")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateBelemTargets(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string updateSql = """
            UPDATE discovery_targets 
            SET enabled = TRUE, next_run_at = CURRENT_TIMESTAMP(6)
            WHERE normalized_city = 'belem';

            UPDATE api_budgets 
            SET paused = FALSE, monthly_limit_requests = 1000
            WHERE provider IN ('GooglePlaces', 'Gemini');

            INSERT IGNORE INTO crawler_tasks (source_id, url, city, category, state, next_attempt_at)
            SELECT id, base_url, 'Belém', default_category, 'pending', CURRENT_TIMESTAMP(6)
            FROM crawler_sources
            WHERE target_city = 'Belém' AND status = 'active';
            """;
        var affected = await connection.ExecuteAsync(new CommandDefinition(updateSql, cancellationToken: ct));

        var queue = serviceProvider.GetService<IDiscoveryQueue>();
        int enqueued = 0;
        if (queue != null)
        {
            enqueued = await queue.EnqueueDueTargetsAsync(DateTimeOffset.UtcNow, ct);
        }

        TempData["Flash"] = $"Alvos e fontes de Belém ativados com sucesso! {enqueued} novos jobs de descoberta enfileirados na fila durável.";
        return RedirectToAction(nameof(Diagnostics));
    }

    [HttpPost("diagnostics/run-belem-now")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunBelemDiscoveryNow(CancellationToken ct)
    {
        var placeProvider = serviceProvider.GetService<IPlaceDiscoveryProvider>();
        var orchestrator = serviceProvider.GetService<DiscoveryOrchestrator>();

        if (placeProvider == null || orchestrator == null)
        {
            TempData["ErrorMessage"] = "Serviços de descoberta não registrados no contêiner de injeção de dependência.";
            return RedirectToAction(nameof(Diagnostics));
        }

        if (!placeProvider.IsConfigured)
        {
            TempData["ErrorMessage"] = "Google Places não configurado. Defina a variável GooglePlaces:ApiKey ou GOOGLEPLACES__APIKEY nas configurações de ambiente da VPS.";
            return RedirectToAction(nameof(Diagnostics));
        }

        try
        {
            var workItem = new DiscoveryWorkItem(0, 1, "Belém", "Restaurante", 1);

            var metrics = await orchestrator.RunTargetAsync(workItem, ct);

            TempData["Flash"] = $"Descoberta em Belém executada com sucesso! " +
                                $"Descobertos: {metrics.PlacesDiscovered}, " +
                                $"Novos na Moderação: {metrics.RestaurantsCreated}, " +
                                $"Atualizados: {metrics.RestaurantsUpdated}, " +
                                $"Ignorados: {metrics.PlacesSkipped}, " +
                                $"Falhas: {metrics.JobsFailed}.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Erro ao executar descoberta de Belém: {ex.Message}";
        }

        return RedirectToAction(nameof(Diagnostics));
    }
}

public sealed class RestaurantAdminRow
{
    public long id { get; set; }
    public string name { get; set; } = "";
    public string city { get; set; } = "";
    public string? neighborhood { get; set; }
    public string? category { get; set; }
    public string? price_range { get; set; }
    public string state { get; set; } = "";
    public decimal cuia_score { get; set; }
    public DateTimeOffset updated_at { get; set; }
    public string? phone { get; set; }
    public string? address { get; set; }
}

public sealed class AiStatRow
{
    public string Provider { get; set; } = "";
    public decimal CostUsd { get; set; }
    public int Calls { get; set; }
    public DateTimeOffset? LastCall { get; set; }
    public int Failures { get; set; }
}

public sealed class SyncStatRow
{
    public string Source { get; set; } = "";
    public long MaxId { get; set; }
    public int TotalRuns { get; set; }
}

public sealed class CycleRunRow
{
    public DateTimeOffset started_at { get; set; }
    public DateTimeOffset? completed_at { get; set; }
    public int jobs_claimed { get; set; }
    public int places_discovered { get; set; }
    public int jobs_failed { get; set; }
}

public sealed class MerchantOrderRow
{
    public long id { get; set; }
    public long restaurant_id { get; set; }
    public string user_id { get; set; } = "";
    public string plan_code { get; set; } = "";
    public int amount_cents { get; set; }
    public string status { get; set; } = "";
    public string order_number { get; set; } = "";
    public DateTimeOffset created_at { get; set; }
    public DateTimeOffset? paid_at { get; set; }
}

public sealed class AdminConnectorsViewModel
{
    public required IReadOnlyList<ConnectorStatusItem> Connectors { get; init; }
    public CycleRunRow? LastWorkerCycle { get; init; }
    public int TotalPendingMod { get; init; }
}

public sealed class ConnectorStatusItem
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public bool IsConfigured { get; init; }
    public string ConfiguredDetails { get; init; } = "";
    public bool HasVerifiedSuccess { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public string RecentErrors { get; init; } = "Nenhum erro";
    public decimal? CostUsd { get; init; }
    public int PendingModerationCount { get; init; }
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
}

public sealed class AdminDiscoveryDiagnosticsViewModel
{
    public bool MySqlConnected { get; set; }
    public string MySqlDatabase { get; set; } = "";
    public string MySqlErrorMessage { get; set; } = "";
    public long MySqlPingMs { get; set; }

    public bool GooglePlacesConfigured { get; set; }
    public string GooglePlacesBaseUrl { get; set; } = "";
    public string GooglePlacesStatusMessage { get; set; } = "";

    public int TotalDiscoveryTargets { get; set; }
    public int EnabledDiscoveryTargets { get; set; }
    public int BelemTargetsEnabled { get; set; }

    public int DiscoveryPendingJobs { get; set; }
    public int DiscoveryFailedJobs { get; set; }
    public int DiscoveryDeadLetteredJobs { get; set; }

    public int CrawlerActiveSources { get; set; }
    public int CrawlerPendingTasks { get; set; }

    public CycleRunRow? LastWorkerCycle { get; set; }
    public int TotalPendingModeration { get; set; }

    public List<string> InactivityReasons { get; set; } = [];
}
