using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// A comment on a task (frame 08). Never hard-deleted: deleting stamps <see cref="DeletedAt"/> with a reason, so the
/// activity log and the conversation stay honest. <see cref="MentionedPersonIds"/> holds the people @mentioned in the
/// body; an <c>email.task_mention</c> outbox message is queued only for those who can see that board.
/// </summary>
public sealed class TaskComment
{
    public const int BodyMaxLength = 4000;
    public const int MaxMentions = 10;
    public const int ReasonMaxLength = 500;

    /// <summary>A comment can be edited by its author for this long; after that the record stands.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(24);

    private TaskComment() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }

    /// <summary>
    /// The author's <b>user</b> id (comments always come from someone signed in). Named and stored as
    /// <c>author_user_id</c> so it is never mistaken for one of the person ids it sits next to.
    /// </summary>
    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; } = default!;
    public List<Guid> MentionedPersonIds { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedById { get; private set; }
    public string? DeleteReason { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static TaskComment Create(Guid taskId, Guid authorUserId, string body, IEnumerable<Guid>? mentionedPersonIds, DateTimeOffset at)
    {
        if (taskId == Guid.Empty) throw new DomainException("A task is required.");
        if (authorUserId == Guid.Empty) throw new DomainException("An author is required.");
        var mentions = (mentionedPersonIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (mentions.Count > MaxMentions) throw new DomainException($"A comment can mention at most {MaxMentions} people.");
        return new TaskComment
        {
            Id = Guid.CreateVersion7(at),
            TaskId = taskId,
            AuthorUserId = authorUserId,
            Body = TaskRules.Required(body, "Comment", BodyMaxLength),
            MentionedPersonIds = mentions,
            CreatedAt = at,
        };
    }

    /// <summary>The author may fix their own comment within <see cref="EditWindow"/>.</summary>
    public bool Edit(string body, DateTimeOffset at)
    {
        EnsureLive();
        if (at - CreatedAt > EditWindow) throw new DomainException("This comment is too old to edit.");
        var newBody = TaskRules.Required(body, "Comment", BodyMaxLength);
        if (newBody == Body) return false;
        Body = newBody;
        EditedAt = at;
        return true;
    }

    /// <summary>Soft delete with a reason: the row stays, the body is kept for the audit trail.</summary>
    public void Delete(string reason, DateTimeOffset at, Guid? actorId)
    {
        EnsureLive();
        DeleteReason = TaskRules.Required(reason, "Reason", ReasonMaxLength);
        DeletedAt = at;
        DeletedById = actorId;
    }

    private void EnsureLive()
    {
        if (IsDeleted) throw new DomainException("This comment was deleted.");
    }
}
