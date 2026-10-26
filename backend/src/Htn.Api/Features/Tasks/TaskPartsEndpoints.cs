using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Committees;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using static Htn.Api.Features.Tasks.TaskDetailEndpoints;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// The parts of the task detail dialog (frame 08): assignees and their points split, the checklist, attachments and
/// comments with @mentions.
/// <para>
/// Assignees need <c>Tasks.Manage</c> (board or the committee lead) because they decide who earns points; the
/// checklist, comments and attachments need <c>Tasks.Work</c> (board, lead or an assignee). Every route carries the
/// policy and then re-runs it against the real <see cref="TaskScope"/>.
/// </para>
/// </summary>
public static class TaskPartsEndpoints
{
    public static RouteGroupBuilder MapTaskParts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tasks/{taskId:guid}").WithTags("Tasks");

        // Every mutating route is metered per signed-in user (see TaskRateLimiting); the download is not.
        g.MapPost("/assignees", AddAssignee).RequireAuthorization(Policies.TasksManage)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<AddAssigneeRequest>();
        g.MapDelete("/assignees/{assigneeId:guid}", RemoveAssignee).RequireAuthorization(Policies.TasksManage)
            .RequireRateLimiting(TaskRateLimiting.Write);

        g.MapPost("/checklist", AddChecklistItem).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<ChecklistItemRequest>();
        g.MapPut("/checklist/{itemId:guid}", UpdateChecklistItem).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<UpdateChecklistItemRequest>();
        g.MapDelete("/checklist/{itemId:guid}", RemoveChecklistItem).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write);
        g.MapPost("/checklist/reorder", ReorderChecklist).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<ReorderChecklistRequest>();

        g.MapPost("/comments", AddComment).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CreateCommentRequest>();
        g.MapPut("/comments/{commentId:guid}", EditComment).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<UpdateCommentRequest>();
        g.MapDelete("/comments/{commentId:guid}", DeleteComment).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<TaskReasonRequest>();

        g.MapPost("/attachments", UploadAttachment).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write);
        // Board viewers may download; the file is always a download, never rendered in the browser.
        g.MapGet("/attachments/{attachmentId:guid}", DownloadAttachment).RequireAuthorization(Policies.TasksViewBoard);
        g.MapPost("/attachments/{attachmentId:guid}/remove", RemoveAttachment).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write);
        return g;
    }

    // ---------- Assignees ----------

    private static async Task<IResult> AddAssignee(
        Guid taskId, AddAssigneeRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ITaskScopeResolver scopes, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        // Who is on the card decides who earns points, so it is written exactly like the approve path: one
        // transaction, the task row locked, the row re-read. An approve landing at the same moment therefore either
        // happens before this (and this gets 409 task_done) or after it (and the new assignee is awarded) — the
        // award is never silently dropped. The task_assignees_frozen trigger is the backstop underneath.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TaskOps.LockAsync(db, task!.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        if (task.IsArchived) return TaskProblems.Archived();
        if (task.Status == TaskBoardStatus.Done) return TaskProblems.Done();

        var now = clock.GetUtcNow();
        var role = req.Role!.Value;
        TaskAssignee assignee;
        string name;

        if (req.PersonId is { } personId)
        {
            if (!await CommitteesEndpoints.IsLevelAsync(db, personId, CommitteeViews.MemberLevels, ct))
                return Problems.Field("personId", "Only an active board member, goon or apprentice with a login can be assigned.",
                    TaskErrorCodes.WrongLevel);
            if (await db.TaskAssignees.AnyAsync(a => a.TaskId == task.Id && a.PersonId == personId, ct))
                return TaskProblems.Conflict("That person is already on this task.", TaskErrorCodes.AlreadyAssigned);
            assignee = TaskAssignee.ForPerson(task, personId, role, now, me.Id);
            name = await db.People.AsNoTracking().Where(p => p.Id == personId)
                .Select(p => p.FirstName + " " + p.LastName).SingleAsync(ct);
        }
        else
        {
            var volunteerId = req.VolunteerId!.Value;
            // A volunteer helps, never leads, and never earns points.
            if (role != TaskAssigneeRole.Assistant)
                return Problems.Field("role", "A volunteer can only help as an Assistant.", TaskErrorCodes.VolunteerRole);
            var volunteer = await db.EventVolunteers.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == volunteerId && v.EventId == task.EventId && v.IsActive, ct);
            if (volunteer is null) return Problems.Field("volunteerId", "Choose an active volunteer of this event.");
            if (await db.TaskAssignees.AnyAsync(a => a.TaskId == task.Id && a.VolunteerId == volunteerId, ct))
                return TaskProblems.Conflict("That volunteer is already on this task.", TaskErrorCodes.AlreadyAssigned);
            assignee = TaskAssignee.ForVolunteer(task, volunteerId, role, now, me.Id);
            name = volunteer.Name;
        }

        db.TaskAssignees.Add(assignee);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.AssigneeAdded, $"added {name} as {role}", now);
        audit.Record(me.Id, "task.assignee_add", nameof(TaskAssignee), assignee.Id.ToString(),
            after: new { assignee.TaskId, assignee.PersonId, assignee.VolunteerId, Role = role.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // The caller may have just put themselves (or someone else) on the card: the cached scope answer, and with it
        // canWork / canApprove, has to be recomputed.
        scopes.Invalidate(task.Id);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    private static async Task<IResult> RemoveAssignee(
        Guid taskId, Guid assigneeId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ITaskScopeResolver scopes, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        // Same shape as AddAssignee and the approve path: lock the task, re-read it, then change who earns points.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TaskOps.LockAsync(db, task!.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        if (task.IsArchived) return TaskProblems.Archived();
        if (task.Status == TaskBoardStatus.Done) return TaskProblems.Done();

        var assignee = await db.TaskAssignees.SingleOrDefaultAsync(a => a.Id == assigneeId && a.TaskId == task.Id, ct);
        if (assignee is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var name = assignee.PersonId is { } personId
            ? await db.People.AsNoTracking().Where(p => p.Id == personId).Select(p => p.FirstName + " " + p.LastName).SingleAsync(ct)
            : await db.EventVolunteers.AsNoTracking().Where(v => v.Id == assignee.VolunteerId).Select(v => v.Name).SingleAsync(ct);

        db.TaskAssignees.Remove(assignee);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.AssigneeRemoved, $"removed {name} from the task", now);
        audit.Record(me.Id, "task.assignee_remove", nameof(TaskAssignee), assignee.Id.ToString(),
            new { assignee.TaskId, assignee.PersonId, assignee.VolunteerId, Role = assignee.Role.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        scopes.Invalidate(task.Id);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Checklist ----------

    private static async Task<IResult> AddChecklistItem(
        Guid taskId, ChecklistItemRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;
        if (await db.TaskChecklistItems.CountAsync(i => i.TaskId == task!.Id, ct) >= TaskChecklistItem.MaxItems)
            return TaskProblems.Conflict($"A checklist can have at most {TaskChecklistItem.MaxItems} steps.",
                TaskErrorCodes.TooManyItems);

        var now = clock.GetUtcNow();
        var order = (await db.TaskChecklistItems.Where(i => i.TaskId == task!.Id).MaxAsync(i => (int?)i.SortOrder, ct) ?? 0) + 1;
        var item = TaskChecklistItem.Create(task!, req.Text, order, now, me.Id);
        db.TaskChecklistItems.Add(item);
        TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.ChecklistAdded, $"added “{item.Text}” to the checklist", now);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    private static async Task<IResult> UpdateChecklistItem(
        Guid taskId, Guid itemId, UpdateChecklistItemRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;
        var item = await db.TaskChecklistItems.SingleOrDefaultAsync(i => i.Id == itemId && i.TaskId == task!.Id, ct);
        if (item is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        if (req.Text is not null) item.Rename(req.Text);
        if (req.IsDone is { } isDone && item.Toggle(isDone, now, me.Id))
            TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.ChecklistToggled,
                $"{(isDone ? "ticked" : "unticked")} “{item.Text}”", now);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task!, me, now, ct);
    }

    private static async Task<IResult> RemoveChecklistItem(
        Guid taskId, Guid itemId, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;
        var item = await db.TaskChecklistItems.SingleOrDefaultAsync(i => i.Id == itemId && i.TaskId == task!.Id, ct);
        if (item is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        db.TaskChecklistItems.Remove(item);
        TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.ChecklistRemoved, $"removed “{item.Text}” from the checklist", now);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    private static async Task<IResult> ReorderChecklist(
        Guid taskId, ReorderChecklistRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;
        var items = await db.TaskChecklistItems.Where(i => i.TaskId == task!.Id).ToListAsync(ct);
        if (req.ItemIds.Any(id => items.All(i => i.Id != id)))
            return Problems.Field("itemIds", "That step isn't on this checklist.");

        var order = 0;
        foreach (var id in req.ItemIds) items.Single(i => i.Id == id).Reorder(++order);
        // Anything the client didn't send keeps its place after the reordered ones.
        foreach (var item in items.Where(i => !req.ItemIds.Contains(i.Id)).OrderBy(i => i.SortOrder)) item.Reorder(++order);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task!, me, clock.GetUtcNow(), ct);
    }

    // ---------- Comments ----------

    private static async Task<IResult> AddComment(
        Guid taskId, CreateCommentRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        MentionEmailLimiter mentionLimiter, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        // Commenting stays open on a finished task: only its points and assignees freeze.
        var (task, error) = await LoadAsync(taskId, Policies.TasksWork, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        if (me.Id is not { } authorId) return TypedResults.Unauthorized();

        var now = clock.GetUtcNow();
        // A mention has to name a real person: anything else is a typo or a probe, and it would show as "Unknown".
        var mentions = (req.MentionPersonIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (mentions.Count > 0)
        {
            var known = await db.People.AsNoTracking().Where(p => mentions.Contains(p.Id)).CountAsync(ct);
            if (known != mentions.Count) return Problems.Field("mentionPersonIds", "Choose people from the list.");
        }
        var comment = TaskComment.Create(task.Id, authorId, req.Body, mentions, now);
        db.TaskComments.Add(comment);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.CommentAdded, "commented on this task", now);
        // Mention emails go only to people who can see this board, and only within the author's hourly budget; the
        // rest are dropped without a word (the response never says who was mailed).
        var (queued, dropped) = await TaskMentions.QueueAsync(db, mentionLimiter, task.EventId, comment.Id, task.Id,
            comment.MentionedPersonIds, me.PersonId, authorId, now, ct);
        if (queued.Count > 0 || dropped > 0)
            audit.Record(me.Id, "task.mentions_notified", nameof(TaskComment), comment.Id.ToString(),
                after: new { taskId = task.Id, personIds = queued, rateLimited = dropped });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tasks/{task.Id}",
            await TaskViews.DetailAsync(db, task, await TaskViews.FlagsAsync(auth, http, db, task, me, ct), me.Id,
                http.User.IsInRole(Roles.Board), now, ct));
    }

    private static async Task<IResult> EditComment(
        Guid taskId, Guid commentId, UpdateCommentRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksWork, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        var comment = await db.TaskComments.SingleOrDefaultAsync(c => c.Id == commentId && c.TaskId == task.Id, ct);
        if (comment is null) return TypedResults.NotFound();
        // Only the author rewrites their own words.
        if (comment.AuthorUserId != me.Id) return TaskProblems.Conflict("Only the author can edit this comment.", TaskErrorCodes.NotAuthor);

        var now = clock.GetUtcNow();
        if (comment.Edit(req.Body, now))
            TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.CommentEdited, "edited a comment", now);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    private static async Task<IResult> DeleteComment(
        Guid taskId, Guid commentId, [Microsoft.AspNetCore.Mvc.FromBody] TaskReasonRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksWork, http, auth, db, ct);
        if (error is not null) return error;
        var comment = await db.TaskComments.SingleOrDefaultAsync(c => c.Id == commentId && c.TaskId == task!.Id, ct);
        if (comment is null) return TypedResults.NotFound();
        if (comment.AuthorUserId != me.Id && !http.User.IsInRole(Roles.Board))
            return TaskProblems.Conflict("Only the author or the board can delete this comment.", TaskErrorCodes.NotAuthor);

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();
        // Soft delete: the row and its body stay for the audit trail, the API stops serving the text.
        comment.Delete(reason, now, me.Id);
        TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.CommentDeleted, "deleted a comment", now);
        audit.Record(me.Id, "task.comment_delete", nameof(TaskComment), comment.Id.ToString(),
            new { comment.TaskId, comment.AuthorUserId }, new { comment.DeletedAt }, reason);
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Attachments ----------

    private static async Task<IResult> UploadAttachment(
        Guid taskId, HttpRequest request, HttpContext http, IAuthorizationService auth, HtnDbContext db, IFileStorage storage,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;

        var (file, uploadError) = await Uploads.ReadSingleFileAsync(request, "file", StoredFilePurposes.TaskAttachmentMaxBytes,
            "The file must be 5 MB or smaller.", ct);
        if (uploadError is not null) return uploadError;
        // Trust the bytes, never the declared type or the file name: PNG, JPG or PDF only.
        if (FileSignatures.DetectAttachment(file!.Bytes) is not { } detected)
            return Problems.Field("file", "Upload a PNG, JPG or PDF.", TaskErrorCodes.UnsupportedType);
        // Every attachment ever uploaded to this task counts, removed ones included: the bytes are kept for ever, so
        // an upload → remove loop must not be a way to write unlimited 5 MB blobs.
        if (await db.TaskAttachments.CountAsync(a => a.TaskId == task!.Id, ct) >= TaskAttachment.MaxPerTask)
            return TaskProblems.Conflict($"A task can hold at most {TaskAttachment.MaxPerTask} attachments.",
                TaskErrorCodes.TooManyAttachments);

        var now = clock.GetUtcNow();
        var (contentType, bytes) = detected;
        var stored = StoredFile.Create(contentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            StoredFilePurposes.TaskAttachment, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, bytes, ct);
        db.StoredFiles.Add(stored);
        var attachment = TaskAttachment.Create(task!, stored.Id, SafeName(file.FileName, contentType), now, me.Id);
        db.TaskAttachments.Add(attachment);
        TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.AttachmentAdded, $"uploaded {attachment.FileName}", now);
        audit.Record(me.Id, "task.attachment_add", nameof(TaskAttachment), attachment.Id.ToString(),
            after: new { attachment.TaskId, fileId = stored.Id, stored.ContentType, stored.SizeBytes, stored.Sha256 });
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    /// <summary>A display name without path separators or control characters; empty names fall back to the type.</summary>
    private static string SafeName(string? fileName, string contentType)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? "");
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/')).ToArray()).Trim();
        if (name.Length == 0)
            name = contentType switch
            {
                FileSignatures.PdfContentType => "attachment.pdf",
                FileSignatures.JpegContentType => "attachment.jpg",
                _ => "attachment.png",
            };
        return name.Length > 200 ? name[..200] : name;
    }

    /// <summary>
    /// Streams the file to anyone who can see the board. Always a download: <c>Content-Disposition: attachment</c> plus
    /// <c>nosniff</c> and a locked-down CSP, so an uploaded PDF or SVG-ish payload can never run in our origin.
    /// </summary>
    private static async Task<IResult> DownloadAttachment(
        Guid taskId, Guid attachmentId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IFileStorage storage,
        CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksViewBoard, http, auth, db, ct);
        if (error is not null) return error;
        var found = await (
            from a in db.TaskAttachments.AsNoTracking()
            join f in db.StoredFiles.AsNoTracking() on a.FileId equals f.Id
            where a.Id == attachmentId && a.TaskId == task!.Id && a.RemovedAt == null
                  && f.Purpose == StoredFilePurposes.TaskAttachment
            select new { f.Id, f.ContentType, f.Sha256, a.FileName }).SingleOrDefaultAsync(ct);
        if (found is null) return TypedResults.NotFound();
        var stream = await storage.OpenReadAsync(found.Id, ct);
        if (stream is null) return TypedResults.NotFound();

        var headers = http.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'";
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["X-Robots-Tag"] = "noindex, nofollow";
        return TypedResults.File(stream, found.ContentType, fileDownloadName: found.FileName,
            entityTag: new EntityTagHeaderValue($"\"{found.Sha256}\""));
    }

    private static async Task<IResult> RemoveAttachment(
        Guid taskId, Guid attachmentId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await WorkableAsync(taskId, http, auth, db, ct);
        if (error is not null) return error;
        var attachment = await db.TaskAttachments
            .SingleOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == task!.Id && a.RemovedAt == null, ct);
        if (attachment is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        // Removing is a timestamp; the stored file itself is never deleted.
        attachment.Remove(now, me.Id);
        TaskOps.Log(db, task!.Id, me.Id, TaskActivityKinds.AttachmentRemoved, $"removed {attachment.FileName}", now);
        audit.Record(me.Id, "task.attachment_remove", nameof(TaskAttachment), attachment.Id.ToString(),
            new { attachment.TaskId, attachment.FileId }, new { attachment.RemovedAt });
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Shared ----------

    /// <summary>Loads a task for <c>Tasks.Work</c> and refuses the frozen states (archived, or Done until reopened).</summary>
    private static async Task<(TaskItem? Task, IResult? Error)> WorkableAsync(
        Guid taskId, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksWork, http, auth, db, ct);
        if (error is not null) return (null, error);
        if (task!.IsArchived) return (null, TaskProblems.Archived());
        if (task.Status == TaskBoardStatus.Done) return (null, TaskProblems.Done());
        return (task, null);
    }
}
