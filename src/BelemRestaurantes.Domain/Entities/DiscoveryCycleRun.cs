namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Uma execução de ciclo do Worker. Alimenta o dashboard de execução e saúde
/// (item 6 do painel) e comprova os números exigidos no item 2.
/// </summary>
public sealed class DiscoveryCycleRun
{
    public long Id { get; private set; }
    public string InstanceId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public int JobsClaimed { get; private set; }
    public int PlacesDiscovered { get; private set; }
    public int RestaurantsCreated { get; private set; }
    public int RestaurantsUpdated { get; private set; }
    public int JobsFailed { get; private set; }
    public int PlacesSkipped { get; private set; }

    /// <summary>Duração total do ciclo; preenchida ao concluir.</summary>
    public TimeSpan? Duration { get; private set; }

    public DiscoveryCycleRun(long id, string instanceId, DateTimeOffset startedAt)
    {
        Id = id;
        InstanceId = instanceId;
        StartedAt = startedAt;
    }

    public void RecordDiscovered(bool isNewRestaurant)
    {
        PlacesDiscovered++;
        if (isNewRestaurant)
        {
            RestaurantsCreated++;
        }
        else
        {
            RestaurantsUpdated++;
        }
    }

    public void RecordFailed() => JobsFailed++;

    /// <summary>Resultado que a fonte trouxe e que foi descartado por violar regra de publicação/licença.</summary>
    public void RecordSkipped() => PlacesSkipped++;

    public void Complete(DateTimeOffset now)
    {
        CompletedAt = now;
        Duration = now - StartedAt;
    }
}

/// <summary>Lançamento de custo por chamada de API de IA ou de fonte.</summary>
public sealed class AiCostEntry
{
    public long Id { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    /// <summary>Custo em micros USD (inteiro) para não usar ponto flutuante em dinheiro.</summary>
    public long CostMicrosUsd { get; private set; }

    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public AiCostEntry(
        long id,
        string provider,
        string operation,
        int inputTokens,
        int outputTokens,
        long costMicrosUsd,
        bool success,
        DateTimeOffset occurredAt,
        string? errorMessage = null)
    {
        Id = id;
        Provider = provider;
        Operation = operation;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostMicrosUsd = costMicrosUsd;
        Success = success;
        OccurredAt = occurredAt;
        ErrorMessage = errorMessage;
    }
}