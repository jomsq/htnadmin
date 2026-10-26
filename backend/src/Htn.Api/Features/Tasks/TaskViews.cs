using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Settings;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// DTO projections, name lookups and activity sentences shared by the board, the detail dialog and "My tasks".
/// Everything here reads; the endpoints decide the permission flags and pass them in.
/// </summary>
internal static class TaskViews
{
    /// <summary>Column names as the design writes them (frame 07), used in the activity log sentences.</summary>
    public static string Label(TaskBoardStatus status) => status switch
    {
        TaskBoardStatus.Backlog => "Backlog",
        TaskBoardStatus.ToDo => "To do",
        TaskBoardStatus.InProgress => "In progress",
        TaskBoardStatus.Review => "Review",
        _ => "Done",
    };

    public static readonly TaskBoardStatus[] Columns =
        [TaskBoardStatus.Backlog, TaskBoardStatus.ToDo, TaskBoardStatus.InProgress, TaskBoardStatus.Review, TaskBoardStatus.Done];

    public static decimal Multiplier(OrgSettings settings, TaskAssigneeRole role) => role switch
    {
        TaskAssigneeRole.Lead => settings.LeadMultiplier,
        TaskAssigneeRole.Collab => settings.CollabMultiplier,
        _ => settings.AssistantMultiplier,
    };

    public static decimal Project(int basePoints, decimal multiplier) =>
        decimal.Round(basePoints * multiplier, 2, MidpointRounding.AwayFromZero);

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>Person id → "First Last" for everyone named in the payload.</summary>
    public static async Task<Dictionary<Guid, string>> PersonNamesAsync(HtnDbContext db, IReadOnlyCollection<Guid> personIds, CancellationToken ct)
    {
        if (personIds.Count == 0) return [];
        return await db.People.AsNoTracking().Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
    }

    /// <summary>User id → "First Last" of the linked person (activity actors, comment authors, uploaders).</summary>
    public static async Task<Dictionary<Guid, string>> ActorNamesAsync(HtnDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        var rows = await (
            from u in db.Users.AsNoTracking()
            join p in db.People.AsNoTracking() on u.PersonId equals (Guid?)p.Id
            where userIds.Contains(u.Id)
            select new { u.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    public static async Task<Dictionary<Guid, string>> VolunteerNamesAsync(HtnDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.EventVolunteers.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Name }).ToDictionaryAsync(v => v.Id, v => v.Name, ct);
    }

    // ---------- Cards ----------

    /// <summary>Everything a batch of cards needs, loaded in four queries instead of one per card.</summary>
    public sealed record CardData(
        Dictionary<Guid, List<TaskAssignee>> Assignees,
        Dictionary<Guid, (int Done, int Total)> Checklist,
        Dictionary<Guid, string> PersonNames,
        Dictionary<Guid, string> VolunteerNames,
        Dictionary<Guid, string> CommitteeNames);

    public static async Task<CardData> CardDataAsync(HtnDbContext db, IReadOnlyList<TaskItem> tasks, CancellationToken ct)
    {
        var ids = tasks.Select(t => t.Id).ToList();
        // Role is stored as text, so the Lead → Collab → Assistant order is applied here, not in SQL.
        var assignees = (await db.TaskAssignees.AsNoTracking().Where(a => ids.Contains(a.TaskId)).ToListAsync(ct))
            .OrderBy(a => a.Role).ThenBy(a => a.AddedAt).ToList();
        var checklist = await db.TaskChecklistItems.AsNoTracking().Where(i => ids.Contains(i.TaskId))
            .GroupBy(i => i.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count(), Done = g.Count(i => i.IsDone) })
            .ToListAsync(ct);
        var committeeIds = tasks.Select(t => t.CommitteeId).Distinct().ToList();
        var committees = await db.Committees.AsNoTracking().Where(c => committeeIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return new CardData(
            assignees.GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.ToList()),
            checklist.ToDictionary(c => c.TaskId, c => (c.Done, c.Total)),
            await PersonNamesAsync(db, assignees.Select(a => a.PersonId).OfType<Guid>().Distinct().ToList(), ct),
            await VolunteerNamesAsync(db, assignees.Select(a => a.VolunteerId).OfType<Guid>().Distinct().ToList(), ct),
            committees);
    }

    public static TaskCardDto Card(TaskItem task, CardData data, DateTimeOffset now, bool canMove, Guid? myPersonId)
    {
        var assignees = data.Assignees.TryGetValue(task.Id, out var list) ? list : [];
        var (done, total) = data.Checklist.TryGetValue(task.Id, out var counts) ? counts : (0, 0);
        var open = task.Status != TaskBoardStatus.Done;
        return new TaskCardDto(
            task.Id, task.Reference, task.Title, task.CommitteeId,
            data.CommitteeNames.TryGetValue(task.CommitteeId, out var committeeName) ? committeeName : "",
            task.DueAt,
            task.DueAt is { } due && open && due >= now && due - now <= TaskItem.DueSoonWindow,
            task.DueAt is { } overdue && open && overdue < now,
            done, total, task.BasePoints, task.Labels, task.SortOrder, canMove,
            myPersonId is { } me && assignees.Any(a => a.PersonId == me),
            task.Status,
            assignees.Select(a => new TaskCardAssigneeDto(Name(a, data), Initials(Name(a, data)), a.Role, a.IsVolunteer)).ToList());
    }

    private static string Name(TaskAssignee a, CardData data) =>
        a.PersonId is { } personId
            ? data.PersonNames.GetValueOrDefault(personId, "Unknown")
            : data.VolunteerNames.GetValueOrDefault(a.VolunteerId!.Value, "Volunteer");

    // ---------- Detail ----------

    public sealed record DetailFlags(bool CanEdit, bool CanWork, bool CanApprove, bool CanComment, bool NeedsOtherApprover);

    public static async Task<TaskDetailDto> DetailAsync(
        HtnDbContext db, TaskItem task, DetailFlags flags, Guid? myUserId, bool isBoard, DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == task.EventId).Select(e => e.Name).SingleAsync(ct);
        var committee = await db.Committees.AsNoTracking().Where(c => c.Id == task.CommitteeId)
            .Select(c => new { c.Name, c.LeadPersonId }).SingleAsync(ct);

        var assignees = (await db.TaskAssignees.AsNoTracking().Where(a => a.TaskId == task.Id).ToListAsync(ct))
            .OrderBy(a => a.Role).ThenBy(a => a.AddedAt).ToList();
        var checklist = await db.TaskChecklistItems.AsNoTracking().Where(i => i.TaskId == task.Id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).ToListAsync(ct);
        var attachments = await (
            from a in db.TaskAttachments.AsNoTracking()
            join f in db.StoredFiles.AsNoTracking() on a.FileId equals f.Id
            where a.TaskId == task.Id && a.RemovedAt == null
            orderby a.UploadedAt, a.Id
            select new { a.Id, a.FileName, f.ContentType, f.SizeBytes, a.UploadedAt, a.UploadedById }).ToListAsync(ct);
        var comments = await db.TaskComments.AsNoTracking().Where(c => c.TaskId == task.Id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);
        var activity = await db.TaskActivities.AsNoTracking().Where(a => a.TaskId == task.Id)
            .OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        // Oldest round first, and inside a round Lead → Collab → Assistant (role is text in SQL, so sorted here).
        var awards = (await db.PointsAwards.AsNoTracking().Where(a => a.TaskId == task.Id).ToListAsync(ct))
            .OrderBy(a => a.AwardedAt).ThenBy(a => a.Role).ThenBy(a => a.Id).ToList();

        var personIds = assignees.Select(a => a.PersonId).OfType<Guid>()
            .Concat(awards.Select(a => a.PersonId))
            .Concat(comments.SelectMany(c => c.MentionedPersonIds))
            .Append(committee.LeadPersonId ?? Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var personNames = await PersonNamesAsync(db, personIds, ct);
        var volunteerNames = await VolunteerNamesAsync(db, assignees.Select(a => a.VolunteerId).OfType<Guid>().Distinct().ToList(), ct);
        var userIds = activity.Select(a => a.ActorId)
            .Concat(comments.Select(c => (Guid?)c.AuthorUserId))
            .Concat(attachments.Select(a => a.UploadedById))
            .Append(task.CreatedById).Append(task.ApprovedById)
            .OfType<Guid>().Distinct().ToList();
        var actorNames = await ActorNamesAsync(db, userIds, ct);

        // "Assigned by" (frame 08): whoever last put someone on the task, otherwise the creator.
        var assignedBy = assignees.OrderByDescending(a => a.AddedAt).Select(a => a.AddedById).FirstOrDefault() ?? task.CreatedById;
        var assignedByName = assignedBy is { } by && actorNames.TryGetValue(by, out var byName)
            ? byName + (committee.LeadPersonId is { } leadId && personNames.GetValueOrDefault(leadId) == byName ? " (lead)" : "")
            : null;

        return new TaskDetailDto(
            task.Id, task.Reference, task.EventId, ev, task.CommitteeId, committee.Name,
            task.Title, task.Description, task.Status, task.BasePoints, task.DueAt, task.Labels, task.IsArchived, task.ArchiveReason,
            task.CreatedAt,
            task.CreatedById is { } creator ? actorNames.GetValueOrDefault(creator) : null,
            assignedByName,
            task.ApprovedAt,
            task.ApprovedById is { } approver ? actorNames.GetValueOrDefault(approver) : null,
            settings.RequireTaskApproval,
            new MultipliersDto(settings.LeadMultiplier, settings.CollabMultiplier, settings.AssistantMultiplier),
            new PresetsDto(settings.PresetSmall, settings.PresetMedium, settings.PresetLarge, settings.PresetXl),
            flags.CanEdit, flags.CanWork, flags.CanApprove, flags.CanComment, flags.NeedsOtherApprover,
            assignees.Select(a =>
            {
                var name = a.PersonId is { } pid
                    ? personNames.GetValueOrDefault(pid, "Unknown")
                    : volunteerNames.GetValueOrDefault(a.VolunteerId!.Value, "Volunteer");
                var multiplier = a.EarnsPoints ? Multiplier(settings, a.Role) : 0m;
                return new TaskAssigneeDto(a.Id, a.PersonId, a.VolunteerId, name, Initials(name), a.Role,
                    multiplier, a.EarnsPoints ? Project(task.BasePoints, multiplier) : 0m, a.IsVolunteer);
            }).ToList(),
            checklist.Select(i => new ChecklistItemDto(i.Id, i.Text, i.IsDone, i.SortOrder)).ToList(),
            attachments.Select(a => new TaskAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt,
                a.UploadedById is { } up ? actorNames.GetValueOrDefault(up) : null,
                $"/api/tasks/{task.Id}/attachments/{a.Id}")).ToList(),
            comments.Select(c =>
            {
                var author = actorNames.GetValueOrDefault(c.AuthorUserId);
                // A deleted comment keeps its row for the audit trail; the body is not served to anyone.
                return new TaskCommentDto(c.Id, author, Initials(author),
                    c.IsDeleted ? "" : c.Body,
                    c.MentionedPersonIds.Select(id => new MentionDto(id, personNames.GetValueOrDefault(id, "Unknown"))).ToList(),
                    c.CreatedAt, c.EditedAt, c.IsDeleted,
                    !c.IsDeleted && myUserId == c.AuthorUserId && now - c.CreatedAt <= TaskComment.EditWindow);
            }).ToList(),
            activity.Select(a => new TaskActivityDto(a.At,
                a.ActorId is { } actor ? actorNames.GetValueOrDefault(actor) : null,
                Initials(a.ActorId is { } actor2 ? actorNames.GetValueOrDefault(actor2) : null), a.Kind, a.Text)).ToList(),
            awards.Select(a => new TaskAwardDto(personNames.GetValueOrDefault(a.PersonId, "Unknown"), a.Role, a.Points,
                a.AwardedAt, a.ReversedAt)).ToList());
    }

    // ---------- Permission flags ----------

    /// <summary>
    /// The flags the detail dialog needs, each from the same policy (and the same rule) the endpoints enforce.
    /// <para>
    /// <c>canApprove</c> is false for the sole Lead of the card, because nobody signs off their own work;
    /// <c>needsOtherApprover</c> then says <b>why</b>, so the dialog can disable Approve with the real explanation
    /// instead of guessing ("another committee lead or a board member has to approve this").
    /// </para>
    /// </summary>
    public static async Task<DetailFlags> FlagsAsync(
        IAuthorizationService auth, HttpContext http, HtnDbContext db, TaskItem task, ICurrentUser me, CancellationToken ct)
    {
        var scope = new TaskScope(task.Id);
        var manage = await auth.AllowsAsync(http, scope, Policies.TasksManage);
        var work = await auth.AllowsAsync(http, scope, Policies.TasksWork);
        var live = !task.IsArchived;
        var open = live && task.Status != TaskBoardStatus.Done;
        var soleLead = open && manage && await TaskOps.NeedsOtherApproverAsync(db, task.Id, me.PersonId, ct);
        return new DetailFlags(
            CanEdit: manage && open,
            CanWork: work && open,
            CanApprove: manage && open && !soleLead,
            CanComment: work && live,
            NeedsOtherApprover: soleLead);
    }
}
