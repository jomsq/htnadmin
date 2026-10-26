using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Committees;
using Htn.Domain.Settings;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// One task (frame 08): the detail dialog, editing, moving between columns, approving into Done — which is the only
/// thing that awards points — reopening (which reverses them with a reason) and archiving.
/// <para>
/// Reading needs <c>Tasks.ViewBoard</c> on the task's event; moving, the checklist, comments and attachments need
/// <c>Tasks.Work</c> (the board, the committee lead or an assignee); editing, assignees, base points, approving,
/// reopening and archiving need <c>Tasks.Manage</c> (the board or the committee lead). Every route carries the policy
/// and then re-runs it against the real <see cref="TaskScope"/>, so a lead can never touch another committee's cards.
/// </para>
/// </summary>
public static class TaskDetailEndpoints
{
    public static RouteGroupBuilder MapTaskDetail(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tasks/{taskId:guid}").WithTags("Tasks");
        g.MapGet("/", Detail).RequireAuthorization(Policies.TasksViewBoard);
        // Every mutating route is metered per signed-in user (see TaskRateLimiting).
        g.MapPut("/", Update).RequireAuthorization(Policies.TasksManage)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<UpdateTaskRequest>();
        g.MapPost("/move", Move).RequireAuthorization(Policies.TasksWork)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<MoveTaskRequest>();
        g.MapPost("/approve", Approve).RequireAuthorization(Policies.TasksManage).RequireRateLimiting(TaskRateLimiting.Write);
        g.MapPost("/reopen", Reopen).RequireAuthorization(Policies.TasksManage)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<ReopenTaskRequest>();
        g.MapPost("/archive", Archive).RequireAuthorization(Policies.TasksManage)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<TaskReasonRequest>();
        g.MapPost("/restore", Restore).RequireAuthorization(Policies.TasksManage).RequireRateLimiting(TaskRateLimiting.Write);
        return g;
    }

    // ---------- Shared loading ----------

    /// <summary>
    /// Loads the task, hides it when the caller can't see its event at all (Drafts without <c>Events.Manage</c>), and
    /// re-runs <paramref name="policy"/> against the real task. Null error means "go ahead".
    /// </summary>
    internal static async Task<(TaskItem? Task, IResult? Error)> LoadAsync(
        Guid taskId, string policy, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return (null, TypedResults.NotFound());
        if (await CommitteeViews.VisibleEventAsync(http, auth, db, task.EventId, ct) is null) return (null, TypedResults.NotFound());
        if (await auth.DenyAsync(http, new TaskScope(task.Id), policy) is { } denied) return (null, denied);
        return (task, null);
    }

    internal static async Task<IResult> OkDetailAsync(
        HtnDbContext db, IAuthorizationService auth, HttpContext http, TaskItem task, ICurrentUser me, DateTimeOffset now,
        CancellationToken ct) =>
        TypedResults.Ok(await TaskViews.DetailAsync(db, task, await TaskViews.FlagsAsync(auth, http, db, task, me, ct), me.Id,
            http.User.IsInRole(Roles.Board), now, ct));

    // ---------- Read ----------

    private static async Task<IResult> Detail(
        Guid taskId, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksViewBoard, http, auth, db, ct);
        if (error is not null) return error;
        return await OkDetailAsync(db, auth, http, task!, me, clock.GetUtcNow(), ct);
    }

    // ---------- Edit ----------

    private static async Task<IResult> Update(
        Guid taskId, UpdateTaskRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ITaskScopeResolver scopes, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        if (task.Status == TaskBoardStatus.Done) return TaskProblems.Done();

        var now = clock.GetUtcNow();
        var before = TaskOps.Audit(task);
        var changes = new List<string>();

        if (req.CommitteeId is { } committeeId && committeeId != task.CommitteeId)
        {
            var target = await db.Committees.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == committeeId && c.EventId == task.EventId, ct);
            if (target is null) return Problems.Field("committeeId", "Choose a committee of this event.");
            if (!target.IsActive) return TaskProblems.Conflict("This committee is deactivated.", TaskErrorCodes.CommitteeInactive);
            // Handing a card to another committee needs the right on that committee too.
            if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.TasksManage) is { } denied) return denied;
            task.MoveToCommittee(committeeId, now, me.Id);
            // The task now belongs to another committee, so the per-request answer cached for this task is stale:
            // without this the canEdit / canWork flags in the response would still describe the old committee.
            scopes.Invalidate(task.Id);
            changes.Add($"moved it to {target.Name}");
        }
        if (req.BasePoints is { } points && points != task.BasePoints)
        {
            task.SetBasePoints(points, now, me.Id);
            TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.PointsChanged, $"set base points to {points}", now);
        }
        if (task.Update(req.Title, req.Description, req.DueAt, req.Labels, now, me.Id)) changes.Add("edited the task");
        if (changes.Count > 0)
            TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Edited, string.Join(" and ", changes), now);

        audit.Record(me.Id, "task.update", nameof(TaskItem), task.Id.ToString(), before, TaskOps.Audit(task));
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Move ----------

    private static async Task<IResult> Move(
        Guid taskId, MoveTaskRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksWork, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        var target = req.Status!.Value;
        // Leaving Done reverses awards and needs a reason: that is POST /reopen, never a plain move.
        if (task.Status == TaskBoardStatus.Done) return TaskProblems.Done();

        if (target == TaskBoardStatus.Done)
        {
            var settings = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
            // With approval required, an assignee can push a card as far as Review; only the lead or an admin finishes it.
            if (settings.RequireTaskApproval
                && !await auth.AllowsAsync(http, new TaskScope(task.Id), Policies.TasksManage))
                return TaskProblems.NeedsApproval();
            return await ApproveCoreAsync(task, http, auth, db, audit, me, clock, ct);
        }

        var now = clock.GetUtcNow();
        var from = task.Status;
        if (from == target && req.SortOrder is null) return await OkDetailAsync(db, auth, http, task, me, now, ct);
        var sortOrder = req.SortOrder ?? await TaskOps.NextSortOrderAsync(db, task.EventId, target, ct);
        task.MoveTo(target, sortOrder, now, me.Id);
        if (from != target)
        {
            TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Moved,
                $"moved it from {TaskViews.Label(from)} to {TaskViews.Label(target)}", now);
            audit.Record(me.Id, "task.move", nameof(TaskItem), task.Id.ToString(),
                new { status = from.ToString() }, new { status = target.ToString() });
        }
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Approve, reopen ----------

    private static async Task<IResult> Approve(
        Guid taskId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        return await ApproveCoreAsync(task, http, auth, db, audit, me, clock, ct);
    }

    /// <summary>
    /// Moves the task into Done and writes the awards in <b>one transaction</b> with the task row locked, so two
    /// approvals landing together produce exactly one set of awards. Already Done is a no-op (the same 200), which is
    /// what makes the whole thing idempotent.
    /// </summary>
    private static async Task<IResult> ApproveCoreAsync(
        TaskItem task, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TaskOps.LockAsync(db, task.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        if (task.IsArchived) return TaskProblems.Archived();
        if (task.Status == TaskBoardStatus.Done)
        {
            await tx.CommitAsync(ct);
            return await OkDetailAsync(db, auth, http, task, me, now, ct);
        }
        // Points go to a Lead, a Collab and an Assistant; a card with nobody leading it isn't finished.
        var leads = await TaskOps.LeadPersonIdsAsync(db, task.Id, ct);
        if (leads.Count == 0) return TaskProblems.NoLead();
        // Nobody signs off their own work: the only Lead on the card can't be the one who finishes it. Reading the
        // leads under the task row lock is what makes this safe against an assignee change landing at the same time.
        if (TaskOps.IsSoleLead(leads, me.PersonId)) return TaskProblems.NeedsOtherApprover();

        var settings = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
        var before = TaskOps.Audit(task);
        task.Approve(now, me.Id);
        var awards = await TaskOps.AwardAsync(db, task, settings, now, me.Id, ct);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Approved,
            awards.Count == 0
                ? "approved it and marked it done"
                : $"approved it and awarded {TaskOps.PointsText(awards)} points", now);
        audit.Record(me.Id, "task.approve", nameof(TaskItem), task.Id.ToString(), before, TaskOps.Audit(task));
        foreach (var award in awards)
            audit.Record(me.Id, "points.award", nameof(PointsAward), award.Id.ToString(), after: TaskOps.Audit(award));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    private static async Task<IResult> Reopen(
        Guid taskId, ReopenTaskRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TaskOps.LockAsync(db, task.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        if (task.Status != TaskBoardStatus.Done)
            return TaskProblems.Conflict("This task isn't done.", TaskErrorCodes.NotDone);

        var target = req.Status ?? TaskBoardStatus.Review;
        var before = TaskOps.Audit(task);
        task.Reopen(target, reason, now, me.Id);
        // Awards are append-only: reopening stamps every live row with the reason instead of deleting it.
        var reversed = await TaskOps.ReverseAsync(db, task.Id, reason, now, me.Id, ct);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Reopened,
            reversed.Count == 0
                ? $"reopened it to {TaskViews.Label(target)}"
                : $"reopened it to {TaskViews.Label(target)} and reversed {TaskOps.PointsText(reversed)} points", now);
        audit.Record(me.Id, "task.reopen", nameof(TaskItem), task.Id.ToString(), before, TaskOps.Audit(task), reason);
        foreach (var award in reversed)
            audit.Record(me.Id, "points.reverse", nameof(PointsAward), award.Id.ToString(), after: TaskOps.Audit(award), reason: reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }

    // ---------- Archive ----------

    private static async Task<IResult> Archive(
        Guid taskId, TaskReasonRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        if (task!.IsArchived) return TaskProblems.Archived();
        // Archiving a card that still holds live awards would hide the points it handed out: reopen it first.
        if (await TaskOps.HasLiveAwardsAsync(db, task.Id, ct)) return TaskProblems.ReopenFirst();

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();
        var before = TaskOps.Audit(task);
        task.Archive(reason, hasLiveAwards: false, now, me.Id);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Archived, "archived it", now);
        audit.Record(me.Id, "task.archive", nameof(TaskItem), task.Id.ToString(), before, TaskOps.Audit(task), reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Restore(
        Guid taskId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (task, error) = await LoadAsync(taskId, Policies.TasksManage, http, auth, db, ct);
        if (error is not null) return error;
        if (!task!.IsArchived) return TaskProblems.Conflict("This task isn't archived.", TaskErrorCodes.NotArchived);

        var now = clock.GetUtcNow();
        var before = TaskOps.Audit(task);
        task.Restore(now, me.Id);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Restored, "restored it to the board", now);
        audit.Record(me.Id, "task.restore", nameof(TaskItem), task.Id.ToString(), before, TaskOps.Audit(task));
        await db.SaveChangesAsync(ct);
        return await OkDetailAsync(db, auth, http, task, me, now, ct);
    }
}
