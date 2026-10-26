using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>One line of a task's checklist (frame 08, with the progress bar above it).</summary>
public sealed class TaskChecklistItem
{
    public const int TextMaxLength = 200;
    public const int MaxItems = 50;

    private TaskChecklistItem() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string Text { get; private set; } = default!;
    public bool IsDone { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset? DoneAt { get; private set; }
    public Guid? DoneById { get; private set; }

    public static TaskChecklistItem Create(TaskItem task, string text, int sortOrder, DateTimeOffset at, Guid? actorId)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.EnsureEditable();
        return new TaskChecklistItem
        {
            Id = Guid.CreateVersion7(at),
            TaskId = task.Id,
            Text = TaskRules.Required(text, "Text", TextMaxLength),
            SortOrder = Order(sortOrder),
            CreatedAt = at,
            CreatedById = actorId,
        };
    }

    public bool Rename(string text)
    {
        var newText = TaskRules.Required(text, "Text", TextMaxLength);
        if (newText == Text) return false;
        Text = newText;
        return true;
    }

    public bool Toggle(bool isDone, DateTimeOffset at, Guid? actorId)
    {
        if (isDone == IsDone) return false;
        IsDone = isDone;
        DoneAt = isDone ? at : null;
        DoneById = isDone ? actorId : null;
        return true;
    }

    public void Reorder(int sortOrder) => SortOrder = Order(sortOrder);

    private static int Order(int sortOrder) =>
        sortOrder is >= 0 and <= TaskItem.MaxSortOrder
            ? sortOrder
            : throw new DomainException($"Sort order must be between 0 and {TaskItem.MaxSortOrder}.");
}
