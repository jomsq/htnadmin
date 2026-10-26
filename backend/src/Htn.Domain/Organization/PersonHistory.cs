namespace Htn.Domain.Organization;

public static class PersonHistoryKinds
{
    public const string Created = "created";
    public const string LevelChanged = "level_changed";
    public const string PositionChanged = "position_changed";
    public const string Deactivated = "deactivated";
}

/// <summary>Append-only history of a person's level, position and status changes.</summary>
public sealed class PersonHistory
{
    private PersonHistory() { }

    internal PersonHistory(Guid personId, DateTimeOffset at, Guid? actorId, string kind, string? fromValue, string? toValue, string? reason)
    {
        Id = Guid.CreateVersion7(at);
        PersonId = personId;
        At = at;
        ActorId = actorId;
        Kind = kind;
        FromValue = fromValue;
        ToValue = toValue;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid PersonId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Kind { get; private set; } = default!;
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    public string? Reason { get; private set; }
}
