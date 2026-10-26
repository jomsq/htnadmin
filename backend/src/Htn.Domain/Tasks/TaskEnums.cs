namespace Htn.Domain.Tasks;

/// <summary>
/// The five board columns of frame 07, in order. Named <c>TaskBoardStatus</c> (not <c>TaskStatus</c>) so it never
/// collides with <see cref="System.Threading.Tasks.TaskStatus"/>; the API serializes the member names
/// (<c>Backlog | ToDo | InProgress | Review | Done</c>).
/// </summary>
public enum TaskBoardStatus
{
    Backlog,
    ToDo,
    InProgress,
    Review,
    Done,
}

/// <summary>
/// What someone does on a task, and which multiplier their award uses (Settings → Points, default 1.0 / 0.7 / 0.3).
/// Volunteers are always <see cref="Assistant"/> and never earn points.
/// </summary>
public enum TaskAssigneeRole
{
    Lead,
    Collab,
    Assistant,
}

/// <summary>
/// Activity log kinds. Stored as these exact strings (the API contract uses them as the <c>kind</c> field), checked by
/// <c>ck_task_activities_kind</c>.
/// </summary>
public static class TaskActivityKinds
{
    public const string Created = "created";
    public const string Edited = "edited";
    public const string Moved = "moved";
    public const string PointsChanged = "points_changed";
    public const string AssigneeAdded = "assignee_added";
    public const string AssigneeRemoved = "assignee_removed";
    public const string ChecklistAdded = "checklist_added";
    public const string ChecklistToggled = "checklist_toggled";
    public const string ChecklistRemoved = "checklist_removed";
    public const string AttachmentAdded = "attachment_added";
    public const string AttachmentRemoved = "attachment_removed";
    public const string CommentAdded = "comment_added";
    public const string CommentEdited = "comment_edited";
    public const string CommentDeleted = "comment_deleted";
    public const string Approved = "approved";
    public const string Reopened = "reopened";
    public const string Archived = "archived";
    public const string Restored = "restored";

    public static readonly string[] All =
    [
        Created, Edited, Moved, PointsChanged, AssigneeAdded, AssigneeRemoved, ChecklistAdded, ChecklistToggled,
        ChecklistRemoved, AttachmentAdded, AttachmentRemoved, CommentAdded, CommentEdited, CommentDeleted, Approved,
        Reopened, Archived, Restored,
    ];
}
