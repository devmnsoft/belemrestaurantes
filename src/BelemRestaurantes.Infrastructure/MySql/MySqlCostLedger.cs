using Dapper;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.MySql;

/// <summary>
/// Contabilidade de custo. O painel de APIs lê daqui, e o roteador de IA
/// consulta antes de gastar.
/// </summary>
public sealed class MySqlCostLedger(MySqlConnectionFactory factory) : ICostLedger
{
    public async Task RecordAsync(AiCostEntry entry, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_cost_entries (
                provider, operation, input_tokens, output_tokens,
                cost_micros_usd, success, error_message, occurred_at)
            VALUES (@provider, @operation, @inputTokens, @outputTokens,
                    @costMicrosUsd, @success, @errorMessage, @occurredAt)
            """,
            new
            {
                provider = entry.Provider,
                operation = entry.Operation,
                inputTokens = entry.InputTokens,
                outputTokens = entry.OutputTokens,
                costMicrosUsd = entry.CostMicrosUsd,
                success = entry.Success,
                errorMessage = entry.ErrorMessage,
                occurredAt = entry.OccurredAt
            },
            cancellationToken: ct));
    }

    public async Task<decimal> MonthToDateUsdAsync(string provider, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var micros = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(cost_micros_usd), 0)
            FROM ai_cost_entries
            WHERE provider = @provider AND occurred_at >= @monthStart
            """,
            new { provider, monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero) },
            cancellationToken: ct));

        return micros / 1_000_000m;
    }

    public async Task<IReadOnlyList<(string Provider, decimal CostUsd)>> BreakdownAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<CostRow>(new CommandDefinition(
            """
            SELECT provider, COALESCE(SUM(cost_micros_usd), 0) AS micros
            FROM ai_cost_entries
            WHERE occurred_at >= @monthStart
            GROUP BY provider
            """,
            new { monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero) },
            cancellationToken: ct));

        return rows.Select(r => (r.provider, r.micros / 1_000_000m)).ToArray();
    }

    public async Task<bool> HasBudgetRemaining(string provider, decimal alreadySpentUsd, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<BudgetRow>(new CommandDefinition(
            "SELECT monthly_limit_micros_usd AS limit_micros, paused FROM api_budgets WHERE provider = @provider",
            new { provider },
            cancellationToken: ct));

        // Sem linha de orçamento configurada, o padrão é não gastar.
        // Falhar aberto aqui custaria dinheiro real sem aprovação.
        if (row is null || row.paused)
        {
            return false;
        }

        if (row.limit_micros == 0)
        {
            return false;
        }

        var limitUsd = row.limit_micros / 1_000_000m;
        return alreadySpentUsd < limitUsd;
    }

    private sealed record CostRow(string provider, long micros);

    private sealed record BudgetRow(long limit_micros, bool paused);
}