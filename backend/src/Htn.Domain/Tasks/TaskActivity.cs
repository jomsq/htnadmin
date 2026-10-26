using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// Append-only log of everything that happened to a task (frame 08's Activity list). Like <c>person_history</c> and
/// <c>audit_entries</c> it is protected by a database trigger, so an UPDATE or DELETE fails even outside the API.
/// <see cref="Text"/> is a server-rendered plain sentence; the UI may also format from <see cref="Kind"/>.
/// </summary>
public sealed class TaskActivity
{
    public const int KindMaxLength = 30;
    public const int TextMaxLength = 300;

    private TaskActivity() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public DateTimeOffset At { get; private set; }

    /// <summary>The actor's user id; null for anything the system did on its own.</summary>
    public Guid? ActorId { get; private set; }

    public string Kind { get; private set; } = default!;
    public string Text { get; private set; } = default!;

    public static TaskActivity Record(Guid taskId, Guid? actorId, string kind, string text, DateTimeOffset at)
    {
        if (taskId == Guid.Empty) throw new DomainException("A task is required.");
        if (!TaskActivityKinds.All.Contains(kind)) throw new DomainException($"Unknown activity kind '{kind}'.");
        var sentence = text?.Trim() ?? "";
        if (sentence.Length == 0) throw new DomainException("Activity text is required.");
        return new TaskActivity
        {
            Id = Guid.CreateVersion7(at),
            TaskId = taskId,
            At = at,
            ActorId = actorId,
            Kind = kind,
            Text = sentence.Length > TextMaxLength ? sentence[..TextMaxLength] : sentence,
        };
    }
}
