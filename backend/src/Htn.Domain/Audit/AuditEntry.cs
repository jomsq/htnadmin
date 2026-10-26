namespace Htn.Domain.Audit;

/// <summary>Append-only record of a sensitive change. There are no mutators by design.</summary>
public sealed class AuditEntry
{
    private AuditEntry() { }

    public Guid Id { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? Reason { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset at, Guid? actorId, string action, string entityType, string entityId,
        string? beforeJson, string? afterJson, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return new AuditEntry
        {
            Id = Guid.CreateVersion7(at),
            At = at,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason,
        };
    }
}
