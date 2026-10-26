using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// A PNG, JPG or PDF on a task (frame 08). The bytes live in file storage behind a <c>StoredFile</c> with purpose
/// <c>task_attachment</c>; the file is only ever served through <c>GET /api/tasks/{id}/attachments/{aid}</c> to board
/// viewers, with <c>nosniff</c> and <c>Content-Disposition: attachment</c>. Removing is a timestamp, never a delete.
/// </summary>
public sealed class TaskAttachment
{
    public const int MaxPerTask = 10;
    public const long MaxBytes = 5 * 1024 * 1024;

    private TaskAttachment() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid FileId { get; private set; }
    public string FileName { get; private set; } = default!;
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid? UploadedById { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid? RemovedById { get; private set; }

    public bool IsRemoved => RemovedAt is not null;

    public static TaskAttachment Create(TaskItem task, Guid fileId, string fileName, DateTimeOffset at, Guid? actorId)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.EnsureEditable();
        if (fileId == Guid.Empty) throw new DomainException("A file is required.");
        return new TaskAttachment
        {
            Id = Guid.CreateVersion7(at),
            TaskId = task.Id,
            FileId = fileId,
            FileName = TaskRules.Required(fileName, "File name", 200),
            UploadedAt = at,
            UploadedById = actorId,
        };
    }

    public void Remove(DateTimeOffset at, Guid? actorId)
    {
        if (IsRemoved) throw new DomainException("This attachment was already removed.");
        RemovedAt = at;
        RemovedById = actorId;
    }
}
