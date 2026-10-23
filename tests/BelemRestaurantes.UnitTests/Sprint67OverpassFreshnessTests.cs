using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Crawler;
using Xunit;

namespace BelemRestaurantes.UnitTests;

/// <summary>Sprint 67 — frescor sticky dos espelhos Overpass: observação por processo da idade dos dados de
/// cada espelho, pulo dentro da janela de validade e adiamento sem custo quando tudo foi confirmado velho.</summary>
public sealed class Sprint67OverpassFreshnessTests
{
    private const string Antigo = "https://espelho-antigo.test/api/interpreter";
    private const string Fresco = "https://espelho-fresco.test/api/interpreter";

    public Sprint67OverpassFreshnessTests() => OverpassBuildingSource.ResetMirrorObservations();

    private static OverpassBuildingSource Source(RecordingHttp http, params string[] endpoints) =>
        new(http, new OverpassOptions
        {
            Endpoints = endpoints,
            MinimumIntervalMilliseconds = 0
        });

    private static string StampAt(DateTimeOffset when) =>
        when.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Osm3s(string stamp, string elements = "[]") =>
        $"{{\"osm3s\":{{\"timestamp_osm_base\":\"{stamp}\"}},\"elements\":{elements}}}";

    private static string ElementComEndereco() =>
        "{\"type\":\"way\",\"id\":55,\"center\":{\"lat\":-1.4700,\"lon\":-48.5000}," +
        "\"tags\":{\"addr:street\":\"Travessa dos Tamoios\",\"addr:housenumber\":\"357\"}}";

    private static SafeDownloadResult Json(string text) => new(true, 200, text, "application/json", null, null, null);
    private static SafeDownloadResult Fail(int code) => new(false, code, null, "text/html", null, null, $"http {code}");

    [Fact]
    public async Task AllMirrorsKnownStale_RespondsWithoutAnyRequest()
    {
        // primeira chamada paga a sonda; a segunda (tudo confirmado velho na janela) não toca na rede
        var http = new RecordingHttp { Handler = _ => Json(Osm3s(StampAt(DateTimeOffset.UtcNow.AddDays(-30)))) };
        var source = Source(http, Antigo);

        var first = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(first.SourceAvailable);
        Assert.True(first.DataStale);
        Assert.Equal(1, http.Hits(Antigo));

        Assert.True(source.AllMirrorsKnownStale);
        var second = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(second.SourceAvailable);
        Assert.True(second.DataStale);
        Assert.Null(second.FailureReason);
        Assert.Empty(second.Elements);
        Assert.Equal(1, http.Hits(Antigo)); // zero requisições na segunda chamada
    }

    [Fact]
    public async Task TwoMirrors_BothConfirmedStaleInTwoCalls_ThirdCostsNothing()
    {
        // independentemente de qual espelho responde primeiro, duas sondas cobrem os dois; daí em diante zero rede
        var http = new RecordingHttp { Handler = _ => Json(Osm3s(StampAt(DateTimeOffset.UtcNow.AddDays(-20)))) };
        var source = Source(http, Antigo, Fresco);
        await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(source.AllMirrorsKnownStale);
        Assert.Equal(2, http.TotalHits());

        var third = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(third.SourceAvailable);
        Assert.True(third.DataStale);
        Assert.Equal(2, http.TotalHits()); // a terceira chamada não consultou ninguém
    }

    [Fact]
    public async Task StaleMirrorIsSkipped_FreshMirrorStillAnswered()
    {
        var http = new RecordingHttp
        {
            Handler = url => url.Contains("espelho-antigo")
                ? Json(Osm3s(StampAt(DateTimeOffset.UtcNow.AddDays(-25))))
                : Json(Osm3s(StampAt(DateTimeOffset.UtcNow.AddDays(-1)), "[" + ElementComEndereco() + "]"))
        };
        var source = Source(http, Antigo, Fresco);
        _ = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None); // primeira sonda: pode responder o próprio espelho antigo
        for (var i = 2; i <= 3; i++)
        {
            var result = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
            Assert.True(result.SourceAvailable);
            Assert.False(result.DataStale); // confirmada a velhice do Antigo, só o Fresco atende
        }
        // o espelho antigo pode ter sido sondado no máximo uma vez (a confirmação o sai do caminho)
        Assert.True(http.Hits(Antigo) <= 1);
        Assert.InRange(http.Hits(Fresco), 1, 3);
    }

    [Fact]
    public async Task WindowExpires_MirrorIsProbedAgainThenSticksAsStale()
    {
        // observação fora da janela deixa de valer: sonda se repete; a resposta velha recoloqueia o pulo
        var now = DateTimeOffset.UtcNow;
        OverpassBuildingSource.RecordMirrorObservation(Antigo, now.AddDays(-30), now.AddMinutes(-61));
        var http = new RecordingHttp { Handler = _ => Json(Osm3s(StampAt(now.AddDays(-30)))) };
        var source = Source(http, Antigo);

        var first = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(first.DataStale);
        Assert.Equal(1, http.Hits(Antigo)); // expirada -> sondou de novo
        Assert.True(source.AllMirrorsKnownStale); // e a nova confirmação devolve o estado sticky

        var second = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.Equal(1, http.Hits(Antigo)); // agora tudo confirmado velho: zero rede
    }

    [Fact]
    public async Task ExpiredStaleObservation_FreshResponseHealsTheMirror()
    {
        var now = DateTimeOffset.UtcNow;
        OverpassBuildingSource.RecordMirrorObservation(Antigo, now.AddDays(-40), now.AddMinutes(-61)); // expirada
        var http = new RecordingHttp
        {
            Handler = _ => Json(Osm3s(StampAt(now.AddDays(-1)), "[" + ElementComEndereco() + "]"))
        };
        var source = Source(http, Antigo);

        var first = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.False(first.DataStale); // sondou (janela expirada) e o espelho voltou fresco
        Assert.Single(first.Elements);
        Assert.False(source.AllMirrorsKnownStale);

        var second = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.False(second.DataStale); // fresco não é pulado: segue atendendo
        Assert.Equal(2, http.Hits(Antigo));
    }

    [Fact]
    public async Task UnreadableTimestamp_IsConservativelyStaleAndSticks()
    {
        var http = new RecordingHttp { Handler = _ => Json("{\"elements\":[]}") }; // sem osm3s -> idade ilegível
        var source = Source(http, Antigo);
        var first = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(first.DataStale);
        Assert.Equal(1, http.Hits(Antigo));
        var second = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.True(second.DataStale);
        Assert.Equal(1, http.Hits(Antigo)); // conservador também cola
    }

    [Fact]
    public async Task ZeroMinuteWindow_DisablesSkipping()
    {
        var http = new RecordingHttp { Handler = _ => Json(Osm3s(StampAt(DateTimeOffset.UtcNow.AddDays(-30)))) };
        var source = new OverpassBuildingSource(http, new OverpassOptions
        {
            Endpoints = new[] { Antigo },
            MinimumIntervalMilliseconds = 0,
            StaleMirrorWindowMinutes = 0
        });
        await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        var second = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.Equal(2, http.Hits(Antigo)); // janela desligada -> sonda sempre
        Assert.True(second.DataStale);
    }

    [Fact]
    public async Task StaleSkipPlusRealFailure_StillReportsUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        OverpassBuildingSource.RecordMirrorObservation(Antigo, now.AddDays(-30), now); // Antigo confirmado velho
        var http = new RecordingHttp
        {
            Handler = url => url.Contains("espelho-fresco") ? Fail(503) : Json(Osm3s(StampAt(now.AddDays(-30))))
        };
        var source = Source(http, Antigo, Fresco);
        var result = await source.BuildingsNearAsync(-1.47, -48.5, 80, CancellationToken.None);
        Assert.False(result.SourceAvailable); // falha real de infraestrutura continua adiável/retryável
        Assert.StartsWith("unavailable(", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal(0, http.Hits(Antigo)); // o velho não foi nem consultado
    }

    [Fact]
    public void ReadBaseTimestamp_IsoAndEpochAreParsed_MissingOrBrokenIsNull()
    {
        var iso = new DateTimeOffset(2026, 9, 1, 2, 16, 18, TimeSpan.Zero);
        Assert.Equal(iso, OverpassBuildingSource.ReadBaseTimestamp(Osm3s(StampAt(iso))));

        var epoch = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(epoch, OverpassBuildingSource.ReadBaseTimestamp(Osm3s(epoch.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))));

        Assert.Null(OverpassBuildingSource.ReadBaseTimestamp(Osm3s("")));
        Assert.Null(OverpassBuildingSource.ReadBaseTimestamp(Osm3s("117921"))); // número pequeno demais para epoch
        Assert.Null(OverpassBuildingSource.ReadBaseTimestamp("{\"elements\":[]}")); // sem osm3s
    }

    /// <summary>Fake que conta hits por host; o handler pode trocar de veredicto entre chamadas (o caso real é
    /// o espelho que envelhece ou atualiza sozinho).</summary>
    private sealed class RecordingHttp : ISafeHttpClient
    {
        private readonly ConcurrentDictionary<string, int> _hits = new();
        public Func<string, SafeDownloadResult> Handler { get; init; } = _ => throw new InvalidOperationException("sem resposta");

        public int Hits(string endpoint)
        {
            var host = new Uri(endpoint).Host;
            return _hits.TryGetValue(host, out var n) ? n : 0;
        }

        public int TotalHits() => _hits.Values.Sum();

        public Task<SafeDownloadResult> DownloadAsync(string url, string? etag = null, string? lastModified = null, int timeoutSeconds = 15, CancellationToken ct = default)
        {
            _hits.AddOrUpdate(new Uri(url).Host, 1, (_, n) => n + 1);
            return Task.FromResult(Handler(url));
        }
    }
}
