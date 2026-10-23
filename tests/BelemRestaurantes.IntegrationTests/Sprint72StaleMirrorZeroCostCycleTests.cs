using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.Crawler;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Worker;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

/// <summary>Sprint 72 — o elo final da cadeia da Sprint 67, provado no nível do ciclo com fila e reserva
/// reais no banco: com todos os espelhos confirmados velhos dentro da janela, a ficha adia em
/// <c>overpass_stale_data</c> SEM consumir unidade do teto diário e SEM nenhuma requisição Overpass.
/// O controle negativo (um espelho ainda não confirmado) mostra o oposto: reserva e exatamente uma
/// requisição ao espelho desconhecido, os confirmados continuam sem ser tocados.</summary>
[Collection("MySql")]
public sealed class Sprint72StaleMirrorZeroCostCycleTests
{
    private const string City = "Belém";

    public Sprint72StaleMirrorZeroCostCycleTests() => OverpassBuildingSource.ResetMirrorObservations();

    private static string StampAt(DateTimeOffset when) =>
        when.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Osm3sStale(string elements = "[]") =>
        $"{{\"osm3s\":{{\"timestamp_osm_base\":\"{StampAt(DateTimeOffset.UtcNow.AddDays(-30))}\"}},\"elements\":{elements}}}";

    private static SafeDownloadResult JsonStale(string text) =>
        new(true, 200, text, "application/json", null, null, null);

    /// <summary>Mesma montagem do ServiceContainer do repositório de testes + sobrepõe as fontes externas:
    /// Nominatim virou stub determinístico e o cliente HTTP seguro virou um contador — o que atravessa
    /// ISafeHttpClient aqui é tráfego Overpass, porque o stub corta o Nominatim pela raiz.</summary>
    private static ServiceProvider BuildCycleProvider(ISafeHttpClient http, INominatimGeocodingSource nominatim, params string[] endpoints)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString(),
                ["MailEncryption:KeyRingPath"] = DatabaseEnvironment.MailKeys
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        // Sobrepós depois do AddInfrastructure: o último registro vence.
        services.AddSingleton(new OverpassOptions
        {
            Endpoints = endpoints,
            MinimumIntervalMilliseconds = 0 // sem cadência artificial no teste
        });
        services.AddSingleton<ISafeHttpClient>(http);
        services.AddSingleton<INominatimGeocodingSource>(nominatim);
        services.AddSingleton(new GeocodingWorkerOptions
        {
            BatchSize = 1,                       // o ciclo processa exatamente a ficha empurrada para o passado
            DailyLimit = 10_000,                 // teto folgado: o teste mede quantas unidades foram de fato consumidas
            OverpassEnabled = true,
            QueryCacheEnabled = true,            // o fato 1 pré-carrega o pool; o replay não consome unidade
            StreetNegativeCacheEnabled = false   // isola este teste da feature de cache negativo
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AllMirrorsConfirmedStale_DefersWithoutReserveAndWithoutAnyOverpassRequest()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var scope = root.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IGeocodingQueue>();

        var token = Guid.NewGuid().ToString("N")[..8];
        var endpoints = new[]
        {
            $"https://s72-a-{token}.test/api/interpreter",
            $"https://s72-b-{token}.test/api/interpreter",
            $"https://s72-c-{token}.test/api/interpreter"
        };
        var street = $"Travessa S72 {token}";
        var address = street + ", São Brás";
        var anchor = new NominatimCandidate(-1.4641, -48.4920, "road", 777001, street, null, City, "Pará");
        var nominatim = new StubNominatim(address, anchor);
        var http = new RecordingHttp(_ => JsonStale(Osm3sStale()));
        long id = 0;
        var t0 = await db.ExecuteScalarAsync<DateTime>("SELECT UTC_TIMESTAMP(6)");
        try
        {
            await ShiftDueTasksForwardAsync(db);
            id = await InsertFixtureRestaurantAsync(db, $"Fixture S72 {token}", address);
            // Outras fichas elegíveis podem estar sem tarefa (resíduos de outras suítes): enfileira em lotes
            // até a da fixture entrar. Aí a ficha vira a única com next_run_at no passado: ClaimAsync
            // (ORDER BY next_run_at, restaurant_id) a pega primeiro.
            for (var guard = 0; ; guard++)
            {
                Assert.True(guard < 100, "a ficha fixture nunca entrou na fila");
                if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geocoding_tasks WHERE restaurant_id=@id", new { id }) == 1) break;
                if (await queue.EnqueueEligibleAsync(50, default) == 0) break;
            }
            // A ficha vira a única vencida (as demais foram fixadas em agora+1 h): ClaimAsync
            // (ORDER BY next_run_at, restaurant_id) a pega primeiro.
            var pushedRows = await db.ExecuteAsync(
                "UPDATE geocoding_tasks SET next_run_at=UTC_TIMESTAMP(6)-INTERVAL 1 HOUR, attempts=0 WHERE restaurant_id=@id", new { id });
            Assert.True(pushedRows == 1, "exatamente a tarefa da fixture deveria ser empurrada para o passado");

            // Sprint 66 — pool pré-julgado pela consulta exata: o ciclo rejoga sem perguntar ao Nominatim nem reservar.
            await queue.StoreCachedPoolAsync(address, City, new[] { anchor }, default);

            // Todos os três espelhos confirmados velhos agora mesmo (janela padrão de 60 min).
            var now = DateTimeOffset.UtcNow;
            foreach (var endpoint in endpoints)
                OverpassBuildingSource.RecordMirrorObservation(endpoint, now.AddDays(-30), now);

            var grantedBefore = await GrantedTodayAsync(db);
            GeocodingCycle cycle;
            using (var cycleProvider = BuildCycleProvider(http, nominatim, endpoints))
            {
                var overpassSource = cycleProvider.GetRequiredService<IOsmBuildingSource>();
                Assert.True(overpassSource.AllMirrorsKnownStale,
                    "pre: todos os espelhos confirmados velhos deveriam acionar o atalho zero-HTTP");
                cycle = await GeocodingHostedService.RunAsync(cycleProvider, CancellationToken.None);
            }
            var grantedAfter = await GrantedTodayAsync(db);
            var myRow = await db.QueryFirstOrDefaultAsync<TaskStatus>(
                "SELECT state AS State, last_status AS LastStatus FROM geocoding_tasks WHERE restaurant_id=@id", new { id });
            var touched = (await db.QueryAsync<TouchedRow>(
                """
                SELECT CAST(restaurant_id AS SIGNED) AS Rid, state AS State, last_status AS LastStatus, attempts AS Attempts
                FROM geocoding_tasks WHERE updated_at >= @t0 ORDER BY updated_at
                """, new { t0 })).ToList();
            var diag = $"hits={http.TotalHits()} nominatimCalls={nominatim.Calls} grantedDelta={grantedAfter - grantedBefore} " +
                       $"ciclo=(P{cycle.Processed}/D{cycle.Deferred}/S{cycle.Skipped}) linha={(myRow is null ? "AUSENTE" : $"{myRow.State}/{myRow.LastStatus}")} " +
                       $"tocadas=[{string.Join(" | ", touched.Select(t => $"{t.Rid}:{t.State}/{t.LastStatus}/a{t.Attempts}"))}]";
            Assert.True(cycle.Processed == 1 && cycle.Deferred == 1, $"deveria processar e adiar exatamente 1 ficha — {diag}");
            Assert.True(http.TotalHits() == 0, $"zero requisições Overpass no ciclo inteiro — {diag}");
            Assert.True(nominatim.Calls == 0, $"o Nominatim nem foi consultado (replay de cache) — {diag}");
            Assert.True(grantedBefore == grantedAfter, $"zero unidade do teto consumida — {diag}");
            Assert.True(myRow is not null, $"a tarefa da fixture deveria existir após o ciclo — {diag}");
            Assert.True(myRow!.State == "pending", $"ficha deveria seguir pendente após o adiamento — {diag}");
            Assert.True(myRow.LastStatus == "overpass_stale_data", $"status esperado overpass_stale_data — {diag}");
        }
        finally
        {
            await RestoreDueTasksAsync(db);
            await CleanUpAsync(db, id, address, t0);
        }
    }

    [Fact]
    public async Task OneMirrorStillUnknown_ReservesAndQueriesOnlyThatMirror()
    {
        using var root = (ServiceProvider)ServiceContainer.Build();
        await root.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        await using var db = await root.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        using var scope = root.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IGeocodingQueue>();

        var token = Guid.NewGuid().ToString("N")[..8];
        var endpoints = new[]
        {
            $"https://s72-a-{token}.test/api/interpreter",
            $"https://s72-b-{token}.test/api/interpreter",
            $"https://s72-c-{token}.test/api/interpreter"
        };
        var confirmed = endpoints.Take(2).ToArray();
        var unknown = endpoints[2];
        var street = $"Travessa S72 {token}";
        var address = street + ", São Brás";
        var anchor = new NominatimCandidate(-1.4641, -48.4920, "road", 777001, street, null, City, "Pará");
        var nominatim = new StubNominatim(address, anchor);
        var http = new RecordingHttp(_ => JsonStale(Osm3sStale()));
        long id = 0;
        var t0 = await db.ExecuteScalarAsync<DateTime>("SELECT UTC_TIMESTAMP(6)");
        try
        {
            await ShiftDueTasksForwardAsync(db);
            id = await InsertFixtureRestaurantAsync(db, $"Fixture S72 {token}", address);
            // Mesma razão do fato anterior: garantir que a fixture entre na fila e seja a única vencida.
            for (var guard = 0; ; guard++)
            {
                Assert.True(guard < 100, "a ficha fixture nunca entrou na fila");
                if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geocoding_tasks WHERE restaurant_id=@id", new { id }) == 1) break;
                if (await queue.EnqueueEligibleAsync(50, default) == 0) break;
            }
            // Mesma garantia do fato anterior: a fixture é a única vencida que o claim vai encontrar.
            var pushedRows = await db.ExecuteAsync(
                "UPDATE geocoding_tasks SET next_run_at=UTC_TIMESTAMP(6)-INTERVAL 1 HOUR, attempts=0 WHERE restaurant_id=@id", new { id });
            Assert.True(pushedRows == 1, "exatamente a tarefa da fixture deveria ser empurrada para o passado");

            // Só dois espelhos confirmados velhos: o terceiro ainda nunca respondeu neste processo.
            var now = DateTimeOffset.UtcNow;
            foreach (var endpoint in confirmed)
                OverpassBuildingSource.RecordMirrorObservation(endpoint, now.AddDays(-30), now);

            var grantedBefore = await GrantedTodayAsync(db);
            GeocodingCycle cycle;
            using (var cycleProvider = BuildCycleProvider(http, nominatim, endpoints))
            {
                var overpassSource = cycleProvider.GetRequiredService<IOsmBuildingSource>();
                Assert.False(overpassSource.AllMirrorsKnownStale,
                    "pre: com um espelho ainda não confirmado, o atalho zero-HTTP deve estar desligado");
                cycle = await GeocodingHostedService.RunAsync(cycleProvider, CancellationToken.None);
            }
            var grantedAfter = await GrantedTodayAsync(db);
            var myRow = await db.QueryFirstOrDefaultAsync<TaskStatus>(
                "SELECT state AS State, last_status AS LastStatus FROM geocoding_tasks WHERE restaurant_id=@id", new { id });
            var touched = (await db.QueryAsync<TouchedRow>(
                """
                SELECT CAST(restaurant_id AS SIGNED) AS Rid, state AS State, last_status AS LastStatus, attempts AS Attempts
                FROM geocoding_tasks WHERE updated_at >= @t0 ORDER BY updated_at
                """, new { t0 })).ToList();
            var diag = $"hits={http.TotalHits()} hitDesconhecido={http.Hits(unknown)} hitA={http.Hits(confirmed[0])} hitB={http.Hits(confirmed[1])} " +
                       $"nominatimCalls={nominatim.Calls} grantedDelta={grantedAfter - grantedBefore} " +
                       $"ciclo=(P{cycle.Processed}/D{cycle.Deferred}/S{cycle.Skipped}) " +
                       $"linha={(myRow is null ? "AUSENTE" : $"{myRow.State}/{myRow.LastStatus}")} " +
                       $"tocadas=[{string.Join(" | ", touched.Select(t => $"{t.Rid}:{t.State}/{t.LastStatus}/a{t.Attempts}"))}]";
            Assert.True(cycle.Processed == 1 && cycle.Deferred == 1, $"deveria processar e adiar exatamente 1 ficha — {diag}");
            Assert.True(http.Hits(unknown) == 1, $"o espelho desconhecido deveria ser sondado uma única vez — {diag}");
            Assert.True(http.Hits(confirmed[0]) == 0, $"espelhos confirmados velhos não devem ser consultados — {diag}");
            Assert.True(http.Hits(confirmed[1]) == 0, $"espelhos confirmados velhos não devem ser consultados — {diag}");
            Assert.True(nominatim.Calls == 1, $"sem cache: consulta real (reservada) ao Nominatim — {diag}");
            Assert.True(grantedBefore + 2 == grantedAfter, $"1 unidade Nominatim + 1 unidade Overpass, nada mais — {diag}");
            Assert.True(myRow is not null, $"a tarefa da fixture deveria existir após o ciclo — {diag}");
            Assert.True(myRow!.State == "pending", $"ficha deveria seguir pendente após o adiamento — {diag}");
            Assert.True(myRow.LastStatus == "overpass_stale_data", $"resposta velha e vazia confirma, não rejeita — {diag}");
        }
        finally
        {
            await RestoreDueTasksAsync(db);
            await CleanUpAsync(db, id, address, t0);
        }
    }

    private sealed class TaskStatus
    {
        public string State { get; set; } = "";
        public string LastStatus { get; set; } = "";
    }

    private sealed class TouchedRow
    {
        public long Rid { get; set; }
        public string State { get; set; } = "";
        public string? LastStatus { get; set; }
        public int Attempts { get; set; }
    }

    private sealed class StubNominatim(string address, NominatimCandidate anchor) : INominatimGeocodingSource
    {
        public int Calls { get; private set; }
        public Task<NominatimSearchResult> SearchAsync(string query, string city, CancellationToken ct)
        {
            Calls++;
            return query == address
                ? Task.FromResult(new NominatimSearchResult(true, false, null, new[] { anchor }))
                : Task.FromResult(new NominatimSearchResult(false, false, "stub_out_of_scope", Array.Empty<NominatimCandidate>()));
        }
    }

    private sealed class RecordingHttp(Func<string, SafeDownloadResult> handler) : ISafeHttpClient
    {
        private readonly ConcurrentDictionary<string, int> _hits = new();
        public int Hits(string endpoint) => _hits.TryGetValue(new Uri(endpoint).Host, out var n) ? n : 0;
        public int TotalHits() => _hits.Values.Sum();
        public Task<SafeDownloadResult> DownloadAsync(string url, string? etag = null, string? lastModified = null,
            int timeoutSeconds = 15, CancellationToken ct = default)
        {
            _hits.AddOrUpdate(new Uri(url).Host, 1, (_, n) => n + 1);
            return Task.FromResult(handler(url));
        }
    }

    private static async Task<int> GrantedTodayAsync(IDbConnection db) =>
        await db.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT granted FROM geocoding_runs WHERE run_day=UTC_DATE()),0)");

    /// <summary>Empurra 1 h para frente apenas os pendências já vencidas (hoje: dezenas de milhares de
    /// adiamentos de cota), guardando os valores originais numa tabela temporária desta conexão para o
    /// restore exato no finally. Com a worker parada, nada mais escreve em geocoding_tasks durante o teste.</summary>
    /// <summary>Fixa em agora+1 h os next_run_at de qualquer pendência vencida (hoje há milhares de adiamentos de
    /// cota e parte deles está com dias vencida — somar 1 h não bastaria), guardando os valores originais numa
    /// tabela temporária desta conexão para o restore exato no finally. Com a worker parada, nada mais escreve em
    /// geocoding_tasks durante o teste.</summary>
    private static async Task ShiftDueTasksForwardAsync(IDbConnection db)
    {
        await db.ExecuteAsync("DROP TEMPORARY TABLE IF EXISTS s72_due_restore");
        await db.ExecuteAsync("CREATE TEMPORARY TABLE s72_due_restore(restaurant_id BIGINT UNSIGNED PRIMARY KEY, due DATETIME(6)) ENGINE=InnoDB");
        await db.ExecuteAsync("INSERT INTO s72_due_restore SELECT restaurant_id, next_run_at FROM geocoding_tasks WHERE next_run_at < UTC_TIMESTAMP(6)");
        await db.ExecuteAsync("UPDATE geocoding_tasks SET next_run_at=UTC_TIMESTAMP(6)+INTERVAL 1 HOUR WHERE next_run_at < UTC_TIMESTAMP(6)");
    }

    private static async Task RestoreDueTasksAsync(IDbConnection db)
    {
        try
        {
            await db.ExecuteAsync("""
                UPDATE geocoding_tasks g JOIN s72_due_restore r ON r.restaurant_id = g.restaurant_id SET g.next_run_at = r.due
                """);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tabela temporária ausente (o shift não chegou a rodar): nada a restaurar.
            if (!ex.Message.Contains("s72_due_restore", StringComparison.OrdinalIgnoreCase)) throw;
        }
        finally
        {
            await db.ExecuteAsync("DROP TEMPORARY TABLE IF EXISTS s72_due_restore");
        }
    }

    private static async Task<long> InsertFixtureRestaurantAsync(IDbConnection db, string name, string address) =>
        await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO restaurants(name,normalized_name,city,normalized_city,address,category,status,created_at,updated_at)
            VALUES(@name,LOWER(@name),'Belém','belem',@address,'Teste','published',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID()
            """, new { name, address }));

    private static async Task CleanUpAsync(IDbConnection db, long id, string address, DateTime t0)
    {
        if (id != 0)
        {
            await db.ExecuteAsync("DELETE FROM restaurant_owners WHERE restaurant_id=@id", new { id });
            await db.ExecuteAsync("DELETE FROM geocoding_tasks WHERE restaurant_id=@id", new { id });
            await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id", new { id });
        }
        // Efeitos colaterais dos runs de depuração: fichas reais que o ciclo consultou no stub (status único do teste).
        // Com a tarefa removida, o próximo ciclo do Worker reenfileira a ficha normalmente, como faria hoje.
        await db.ExecuteAsync("DELETE FROM geocoding_tasks WHERE last_status='stub_out_of_scope'");
        await db.ExecuteAsync("DELETE FROM geocoding_query_cache WHERE created_at >= @t0", new { t0 });
        await db.ExecuteAsync("DELETE FROM overpass_mirror_freshness WHERE endpoint LIKE 'https://s72-%.test/api/interpreter'");
    }
}
