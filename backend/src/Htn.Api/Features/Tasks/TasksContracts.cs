using Htn.Domain.Tasks;

namespace Htn.Api.Features.Tasks;

// ---------- Board (frame 07) ----------

/// <summary>
/// The whole task board of one event: the committee filter chips, the five columns and their cards. Permission flags
/// are explicit (<c>canCreateTask</c>, per-committee <c>canManage</c>, per-card <c>canMove</c>) because the four
/// committee/task policies are resource-scoped and <c>/api/auth/me</c> can only say "may be allowed".
/// </summary>
public sealed record TaskBoardDto(bool CanCreateTask, IReadOnlyList<BoardCommitteeDto> Committees, IReadOnlyList<TaskColumnDto> Columns);

public sealed record BoardCommitteeDto(Guid Id, string Name, bool CanManage);

/// <summary>
/// One column of the board. <c>Count</c> is how many cards the column really holds and <c>HasMore</c> says whether
/// <c>Cards</c> is only part of it: the Done column is capped at the newest <see cref="TaskBoardLimits.DoneCards"/>
/// cards (a finished event's Done pile grows without end), every other column is complete.
/// </summary>
public sealed record TaskColumnDto(TaskBoardStatus Status, int Count, bool HasMore, IReadOnlyList<TaskCardDto> Cards);

public static class TaskBoardLimits
{
    /// <summary>How many Done cards the board returns, newest first.</summary>
    public const int DoneCards = 50;
}

public sealed record TaskCardDto(
    Guid Id, string Reference, string Title, Guid CommitteeId, string CommitteeName,
    DateTimeOffset? DueAt, bool DueSoon, bool Overdue, int ChecklistDone, int ChecklistTotal, int BasePoints,
    IReadOnlyList<string> Labels, int SortOrder, bool CanMove, bool IsMine, TaskBoardStatus Status,
    IReadOnlyList<TaskCardAssigneeDto> Assignees);

public sealed record TaskCardAssigneeDto(string Name, string Initials, TaskAssigneeRole Role, bool IsVolunteer);

/// <summary>"My tasks" in the sidebar and on <c>/my-tasks</c>: the caller's open cards, grouped by event.</summary>
public sealed record MyTasksDto(IReadOnlyList<MyTasksGroupDto> Groups, int OpenCount);

public sealed record MyTasksGroupDto(Guid EventId, string EventName, IReadOnlyList<TaskCardDto> Tasks);

// ---------- Detail (frame 08) ----------

public sealed record TaskDetailDto(
    Guid Id, string Reference, Guid EventId, string EventName, Guid CommitteeId, string CommitteeName,
    string Title, string? Description, TaskBoardStatus Status, int BasePoints, DateTimeOffset? DueAt,
    IReadOnlyList<string> Labels, bool IsArchived, string? ArchiveReason,
    DateTimeOffset CreatedAt, string? CreatedByName, string? AssignedByName,
    DateTimeOffset? ApprovedAt, string? ApprovedByName,
    bool RequireTaskApproval, MultipliersDto Multipliers, PresetsDto Presets,
    bool CanEdit, bool CanWork, bool CanApprove, bool CanComment,
    // needsOtherApprover: this caller holds Tasks.Manage but is the only Lead on the card, so canApprove is false
    // only because nobody signs off their own work. Another lead of the committee, or any board member, has to
    // approve it (409 needs_other_approver if they try anyway).
    bool NeedsOtherApprover,
    IReadOnlyList<TaskAssigneeDto> Assignees, IReadOnlyList<ChecklistItemDto> Checklist,
    IReadOnlyList<TaskAttachmentDto> Attachments, IReadOnlyList<TaskCommentDto> Comments,
    IReadOnlyList<TaskActivityDto> Activity, IReadOnlyList<TaskAwardDto> Awards);

public sealed record MultipliersDto(decimal Lead, decimal Collab, decimal Assistant);

public sealed record PresetsDto(int Small, int Medium, int Large, int Xl);

/// <summary><c>Points</c> is the projected award (base × multiplier, 2 dp). Volunteers never earn points, so 0.</summary>
public sealed record TaskAssigneeDto(
    Guid Id, Guid? PersonId, Guid? VolunteerId, string Name, string Initials, TaskAssigneeRole Role,
    decimal Multiplier, decimal Points, bool IsVolunteer);

public sealed record ChecklistItemDto(Guid Id, string Text, bool IsDone, int SortOrder);

public sealed record TaskAttachmentDto(
    Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, string? UploadedByName, string Url);

public sealed record TaskCommentDto(
    Guid Id, string? AuthorName, string AuthorInitials, string Body, IReadOnlyList<MentionDto> Mentions,
    DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, bool IsDeleted, bool CanEdit);

public sealed record MentionDto(Guid PersonId, string Name);

public sealed record TaskActivityDto(DateTimeOffset At, string? ActorName, string ActorInitials, string Kind, string Text);

/// <summary>What was actually awarded on this task. A reversed row keeps its <c>reversedAt</c> stamp: nothing is deleted.</summary>
public sealed record TaskAwardDto(string Name, TaskAssigneeRole Role, decimal Points, DateTimeOffset AwardedAt, DateTimeOffset? ReversedAt);

// ---------- Requests ----------

public sealed record CreateTaskRequest(
    Guid? CommitteeId, string Title, string? Description, TaskBoardStatus? Status, int? BasePoints,
    DateTimeOffset? DueAt, IReadOnlyList<string>? Labels);

public sealed record UpdateTaskRequest(
    string Title, string? Description, DateTimeOffset? DueAt, IReadOnlyList<string>? Labels, int? BasePoints, Guid? CommitteeId);

public sealed record MoveTaskRequest(TaskBoardStatus? Status, int? SortOrder);

public sealed record ReopenTaskRequest(string Reason, TaskBoardStatus? Status);

public sealed record TaskReasonRequest(string Reason);

public sealed record AddAssigneeRequest(Guid? PersonId, Guid? VolunteerId, TaskAssigneeRole? Role);

public sealed record ChecklistItemRequest(string Text);

public sealed record UpdateChecklistItemRequest(string? Text, bool? IsDone);

public sealed record ReorderChecklistRequest(IReadOnlyList<Guid> ItemIds);

public sealed record CreateCommentRequest(string Body, IReadOnlyList<Guid>? MentionPersonIds);

public sealed record UpdateCommentRequest(string Body);

public static class TaskErrorCodes
{
    public const string TaskDone = "task_done";
    public const string TaskArchived = "task_archived";
    public const string NotArchived = "not_archived";
    public const string NotDone = "not_done";
    public const string NeedsApproval = "needs_approval";
    public const string NoLead = "no_lead";
    public const string NeedsOtherApprover = "needs_other_approver";
    public const string ReopenFirst = "reopen_first";
    public const string AlreadyAssigned = "already_assigned";
    public const string VolunteerRole = "volunteer_role";
    public const string NotAuthor = "not_author";
    public const string UnsupportedType = "unsupported_type";
    public const string FileTooLarge = "file_too_large";
    public const string TooManyAttachments = "too_many_attachments";
    public const string TooManyItems = "too_many_items";
    public const string CommitteeInactive = "committee_inactive";
    public const string WrongLevel = "wrong_level";
}
