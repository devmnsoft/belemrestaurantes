namespace BelemRestaurantes.Domain.Entities;

/// <summary>
/// Registro imutável de decisão administrativa. Escrito dentro da mesma
/// transação da ação, para que não exista mudança sem rastro.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; private set; }
    public string? ActorSubject { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public long? EntityId { get; private set; }
    public string? MetadataJson { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public AuditEvent(
        long id,
        string? actorSubject,
        string action,
        string entityType,
        long? entityId,
        DateTimeOffset occurredAt,
        string? metadataJson = null,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Ação é obrigatória.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Tipo da entidade é obrigatório.", nameof(entityType));
        }

        Id = id;
        ActorSubject = actorSubject;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OccurredAt = occurredAt;
        MetadataJson = metadataJson;
        IpAddress = ipAddress;
    }
}