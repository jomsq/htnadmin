namespace Htn.Domain.Outbox;

public enum OutboxStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// A side effect (e.g. an email) written in the same transaction as the change that caused it,
/// and delivered later by the outbox worker.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 5;
    public const int LastErrorMaxLength = 2000;

    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;

    /// <summary>
    /// Short-lived secret needed only to render the message (e.g. a raw invite token).
    /// Kept out of <see cref="PayloadJson"/> and wiped once the message reaches a final state.
    /// </summary>
    public string? Secret { get; private set; }

    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? ExternalId { get; private set; }

    public static OutboxMessage Create(string type, string payloadJson, string? secret, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(at),
            Type = type,
            PayloadJson = payloadJson,
            Secret = secret,
            Status = OutboxStatus.Pending,
            CreatedAt = at,
            NextAttemptAt = at,
        };
    }

    public void MarkSent(DateTimeOffset at, string? externalId)
    {
        if (Status != OutboxStatus.Pending) throw new InvalidOperationException($"Outbox message is {Status}.");
        Attempts++;
        Status = OutboxStatus.Sent;
        SentAt = at;
        ExternalId = externalId;
        LastError = null;
        Secret = null;
    }

    /// <summary>Records a failed attempt. After <see cref="MaxAttempts"/> the message is flagged Failed for admin review.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset at)
    {
        if (Status != OutboxStatus.Pending) throw new InvalidOperationException($"Outbox message is {Status}.");
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            Secret = null;
            return;
        }
        // 30s, 1m, 2m, 4m ...
        NextAttemptAt = at + TimeSpan.FromSeconds(30 * Math.Pow(2, Attempts - 1));
    }
}
