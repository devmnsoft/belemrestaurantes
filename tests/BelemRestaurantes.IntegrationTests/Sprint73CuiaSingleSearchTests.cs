using System.Collections.Generic;
using System.Text.Json;
using BelemRestaurantes.Application;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BelemRestaurantes.IntegrationTests;

// Sprint 73 — a mesma pergunta não pode pagar a web duas vezes: a etapa culinária e a etapa principal
// compartilhavam a mesma pergunta e executavam buscas completas independentes (cota + tempo em dobro).
[Collection("MySql")]
public sealed class Sprint73CuiaSingleSearchTests
{
    private sealed class CountingWebSearch : IWebSearch
    {
        public int Calls;
        public string MarkerUrl { get; } = "https://s73-marker.example/" + Guid.NewGuid().ToString("N");

        public Task<WebSearchResult> SearchAsync(string query, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new WebSearchResult("ok",
                [new WebSearchSource("Referência de sonda S73", MarkerUrl, "Fonte controlada pelo teste.", DateTimeOffset.UtcNow) { Publisher = "marker", SourceKind = "test_marker" }])
            { Provider = "counting" });
        }
    }

    [Fact]
    public async Task OneCulinaryQuestionRunsWebSearchExactlyOnce()
    {
        var counting = new CountingWebSearch();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MySql"] = DatabaseEnvironment.ConnectionString()!,
            ["MailEncryption:KeyRingPath"] = DatabaseEnvironment.MailKeys
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.RemoveAll<IWebSearch>();
        services.AddSingleton<IWebSearch>(counting);
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<BelemRestaurantes.Infrastructure.Migrations.MigrationRunner>().ApplyAsync(default);
        await using var db = await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try
        {
            var cuia = sp.GetRequiredService<ICuiaAiService>();
            var answer = await cuia.AskAsync(new CuiaAiQuery("Onde comer tacacá em Belém?", SearchWeb: true), default);
            Assert.True(counting.Calls == 1, "a pesquisa externa deveria rodar uma única vez na consulta — foram " + counting.Calls);
            Assert.False(string.IsNullOrWhiteSpace(answer.AnswerMarkdown));
        }
        finally
        {
            // A etapa culinária enfileira URLs descobertas para verificação do robô; limpa o rastro da sonda.
            await db.ExecuteAsync("DELETE FROM culinary_tasks WHERE url=@url", new { url = counting.MarkerUrl });
        }
    }
}
