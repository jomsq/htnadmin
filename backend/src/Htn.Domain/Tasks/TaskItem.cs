using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// A card on an event's task board (frames 07 and 08). Named <c>TaskItem</c> in code (the table is <c>tasks</c>) so it
/// never collides with <see cref="System.Threading.Tasks.Task"/>.
/// <para>
/// The reference shown everywhere (<c>HTN-142</c>) is <see cref="ReferencePrefix"/> plus a per-event sequence
/// <see cref="Number"/>, taken from the counter on the event row and bumped under the event row lock, so two people
/// creating a task at the same time can never get the same number (<c>ux_tasks_event_number</c> is the backstop).
/// </para>
/// <para>
/// Points live in <see cref="PointsAward"/>; they are written when the task is approved into Done and reversed when it
/// is reopened. While the task is Done its base points, assignees and content are frozen: reopen first.
/// </para>
/// </summary>
public sealed class TaskItem
{
    public const string ReferencePrefix = "HTN";
    public const int TitleMaxLength = 140;
    public const int DescriptionMaxLength = 4000;
    public const int MaxBasePoints = 1000;
    public const int MaxLabels = 5;
    public const int LabelMaxLength = 30;
    public const int ReasonMaxLength = 500;
    public const int MaxSortOrder = 1_000_000;
    public const int ReferenceMaxLength = 20;

    /// <summary>A card is "due soon" within this window (frame 07's amber chip).</summary>
    public static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(3);

    private TaskItem() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CommitteeId { get; private set; }

    /// <summary>Per-event sequence number from the event's task counter.</summary>
    public int Number { get; private set; }

    /// <summary>Denormalized <c>HTN-142</c> form of <see cref="Number"/>, so lists and search need no formatting.</summary>
    public string Reference { get; private set; } = default!;

    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public TaskBoardStatus Status { get; private set; }
    public int BasePoints { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public List<string> Labels { get; private set; } = [];

    /// <summary>Position inside its column (frame 07 drag and drop).</summary>
    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedById { get; private set; }

    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public Guid? ArchivedById { get; private set; }
    public string? ArchiveReason { get; private set; }

    /// <summary>Open = on the board and not finished. Drives the committee open-task counts and the deactivate guard.</summary>
    public bool IsOpen => !IsArchived && Status != TaskBoardStatus.Done;

    public static TaskItem Create(
        Guid eventId, Guid committeeId, int number, string title, string? description, TaskBoardStatus status, int basePoints,
        DateTimeOffset? dueAt, IEnumerable<string>? labels, int sortOrder, DateTimeOffset at, Guid? actorId)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        if (committeeId == Guid.Empty) throw new DomainException("A committee is required.");
        if (number <= 0) throw new DomainException("A task number is required.");
        if (!Enum.IsDefined(status)) throw new DomainException("Unknown task status.");
        if (status == TaskBoardStatus.Done) throw new DomainException("A new task can't start in Done.");
        return new TaskItem
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            CommitteeId = committeeId,
            Number = number,
            Reference = $"{ReferencePrefix}-{number}",
            Title = TaskRules.Required(title, "Title", TitleMaxLength),
            Description = TaskRules.Optional(description, "Description", DescriptionMaxLength),
            Status = status,
            BasePoints = Points(basePoints),
            DueAt = dueAt,
            Labels = TaskRules.CleanLabels(labels),
            SortOrder = Order(sortOrder),
            CreatedAt = at,
            CreatedById = actorId,
        };
    }

    /// <summary>Title, description, due date and labels. Refused while Done. Returns true when anything changed.</summary>
    public bool Update(string title, string? description, DateTimeOffset? dueAt, IEnumerable<string>? labels, DateTimeOffset at, Guid? actorId)
    {
        EnsureEditable();
        var newTitle = TaskRules.Required(title, "Title", TitleMaxLength);
        var newDescription = TaskRules.Optional(description, "Description", DescriptionMaxLength);
        var newLabels = TaskRules.CleanLabels(labels);
        if (newTitle == Title && newDescription == Description && dueAt == DueAt && newLabels.SequenceEqual(Labels)) return false;
        Title = newTitle;
        Description = newDescription;
        DueAt = dueAt;
        Labels = newLabels;
        Touch(at, actorId);
        return true;
    }

    /// <summary>Moves the card to another committee of the same event (<c>Tasks.Manage</c> on both sides).</summary>
    public bool MoveToCommittee(Guid committeeId, DateTimeOffset at, Guid? actorId)
    {
        EnsureEditable();
        if (committeeId == Guid.Empty) throw new DomainException("A committee is required.");
        if (committeeId == CommitteeId) return false;
        CommitteeId = committeeId;
        Touch(at, actorId);
        return true;
    }

    /// <summary>Base points, which decide every award. Admin and lead only, and never while Done.</summary>
    public bool SetBasePoints(int basePoints, DateTimeOffset at, Guid? actorId)
    {
        EnsureEditable();
        var points = Points(basePoints);
        if (points == BasePoints) return false;
        BasePoints = points;
        Touch(at, actorId);
        return true;
    }

    /// <summary>
    /// Moves the card between columns. Done is not reachable here: finishing goes through <see cref="Approve"/> and
    /// leaving Done through <see cref="Reopen"/>, which reverses the awards with a reason.
    /// </summary>
    public void MoveTo(TaskBoardStatus status, int sortOrder, DateTimeOffset at, Guid? actorId)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Unknown task status.");
        EnsureNotArchived();
        if (Status == TaskBoardStatus.Done) throw new DomainException("This task is done. Reopen it first.");
        if (status == TaskBoardStatus.Done) throw new DomainException("Approve the task to move it to Done.");
        Status = status;
        SortOrder = Order(sortOrder);
        Touch(at, actorId);
    }

    /// <summary>Only the sort order inside the current column changed.</summary>
    public void Reorder(int sortOrder, DateTimeOffset at, Guid? actorId)
    {
        EnsureNotArchived();
        SortOrder = Order(sortOrder);
        Touch(at, actorId);
    }

    /// <summary>
    /// Approves the task into Done. The caller has checked the policy (<c>Tasks.Manage</c> while approval is required)
    /// and that a Lead assignee exists, and writes the awards in the same transaction.
    /// </summary>
    public void Approve(DateTimeOffset at, Guid? actorId)
    {
        EnsureNotArchived();
        if (Status == TaskBoardStatus.Done) throw new DomainException("This task is already done.");
        Status = TaskBoardStatus.Done;
        ApprovedAt = at;
        ApprovedById = actorId;
        Touch(at, actorId);
    }

    /// <summary>Takes the task out of Done. The caller reverses the awards with the same reason.</summary>
    public void Reopen(TaskBoardStatus status, string reason, DateTimeOffset at, Guid? actorId)
    {
        EnsureNotArchived();
        if (Status != TaskBoardStatus.Done) throw new DomainException("This task isn't done.");
        if (!Enum.IsDefined(status)) throw new DomainException("Unknown task status.");
        if (status == TaskBoardStatus.Done) throw new DomainException("Choose a column other than Done.");
        TaskRules.Required(reason, "Reason", ReasonMaxLength);
        Status = status;
        ApprovedAt = null;
        ApprovedById = null;
        Touch(at, actorId);
    }

    /// <summary>Takes the card off the board with a reason. Nothing is deleted.</summary>
    public void Archive(string reason, DateTimeOffset at, Guid? actorId)
    {
        if (IsArchived) throw new DomainException("This task is already archived.");
        // The reason is checked before anything changes, so a refused archive leaves the card untouched.
        ArchiveReason = TaskRules.Required(reason, "Reason", ReasonMaxLength);
        IsArchived = true;
        ArchivedAt = at;
        ArchivedById = actorId;
        Touch(at, actorId);
    }

    public void Restore(DateTimeOffset at, Guid? actorId)
    {
        if (!IsArchived) throw new DomainException("This task isn't archived.");
        IsArchived = false;
        ArchivedAt = null;
        ArchivedById = null;
        ArchiveReason = null;
        Touch(at, actorId);
    }

    /// <summary>Content, base points and assignees are frozen while the task is Done.</summary>
    public void EnsureEditable()
    {
        EnsureNotArchived();
        if (Status == TaskBoardStatus.Done) throw new DomainException("This task is done. Reopen it first.");
    }

    private void EnsureNotArchived()
    {
        if (IsArchived) throw new DomainException("This task is archived.");
    }

    private void Touch(DateTimeOffset at, Guid? actorId)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    public static bool IsValidBasePoints(int points) => points is >= 0 and <= MaxBasePoints;

    private static int Points(int points) =>
        IsValidBasePoints(points) ? points : throw new DomainException($"Base points must be between 0 and {MaxBasePoints}.");

    private static int Order(int sortOrder) =>
        sortOrder is >= 0 and <= MaxSortOrder ? sortOrder : throw new DomainException($"Sort order must be between 0 and {MaxSortOrder}.");
}

/// <summary>Shared text and label rules for the task entities.</summary>
public static class TaskRules
{
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    /// <summary>Trimmed, non-empty, de-duplicated case-insensitively, at most <see cref="TaskItem.MaxLabels"/>.</summary>
    public static List<string> CleanLabels(IEnumerable<string>? labels)
    {
        if (labels is null) return [];
        var cleaned = new List<string>();
        foreach (var raw in labels)
        {
            var label = raw?.Trim();
            if (string.IsNullOrEmpty(label)) continue;
            if (label.Length > TaskItem.LabelMaxLength)
                throw new DomainException($"A label must be at most {TaskItem.LabelMaxLength} characters.");
            if (!cleaned.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase))) cleaned.Add(label);
        }
        if (cleaned.Count > TaskItem.MaxLabels) throw new DomainException($"A task can have at most {TaskItem.MaxLabels} labels.");
        return cleaned;
    }
}
