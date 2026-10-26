using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Committees;
using Htn.Api.Features.Events;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// The task board of one event (frame 07) and "My tasks" (the sidebar item). Reading the board needs
/// <c>Tasks.ViewBoard</c> — the board, or anyone on a committee of that event; creating a card needs
/// <c>Tasks.Manage</c> on the target committee, i.e. the board or that committee's lead.
/// <para>
/// The route policy is the coarse gate; the real committee is only known once the body or the row is read, so each
/// handler re-runs the same policy against a <see cref="CommitteeScope"/> / <see cref="EventScope"/> /
/// <see cref="TaskScope"/> resource and returns 403 through <see cref="ScopedAuthorization.DenyAsync"/>. Payloads
/// carry explicit <c>canCreateTask</c> / <c>canMove</c> / <c>canManage</c> flags for the UI.
/// </para>
/// </summary>
public static class TasksEndpoints
{
    public static RouteGroupBuilder MapTasks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/tasks").WithTags("Tasks");
        g.MapGet("/", Board).RequireAuthorization(Policies.TasksViewBoard);
        g.MapPost("/", Create).RequireAuthorization(Policies.TasksManage).Validate<CreateTaskRequest>();
        return g;
    }

    /// <summary>
    /// The caller's own open cards across every event. Every signed-in staff role may ask (the answer only ever
    /// contains tasks they are assigned to), so the gate is the organization-wide <c>Leaderboard.View</c> rather than
    /// the event-scoped task policies: "My tasks" is a sidebar screen, not a board of one event.
    /// </summary>
    public static RouteGroupBuilder MapMyTasks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tasks").WithTags("Tasks");
        g.MapGet("/mine", Mine).RequireAuthorization(Policies.LeaderboardView);
        return g;
    }

    // ---------- Read ----------

    private static async Task<IResult> Board(
        Guid eventId, Guid? committeeId, bool? mine, bool? includeArchived, HttpContext http, IAuthorizationService auth,
        HtnDbContext db, ICommitteeContext scope, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (await CommitteeViews.VisibleEventAsync(http, auth, db, eventId, ct) is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.TasksViewBoard) is { } denied) return denied;

        var board = http.User.IsInRole(Roles.Board);
        var committees = await db.Committees.AsNoTracking().Where(c => c.EventId == eventId && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var chips = new List<BoardCommitteeDto>(committees.Count);
        foreach (var c in committees)
            chips.Add(new BoardCommitteeDto(c.Id, c.Name, board || await scope.IsLeadOfAsync(c.Id, ct)));

        var query = db.Tasks.AsNoTracking().Where(t => t.EventId == eventId);
        if (includeArchived != true) query = query.Where(t => !t.IsArchived);
        if (committeeId is { } cid) query = query.Where(t => t.CommitteeId == cid);
        if (mine == true)
        {
            var personId = me.PersonId;
            query = query.Where(t => db.TaskAssignees.Any(a => a.TaskId == t.Id && a.PersonId == personId));
        }
        var tasks = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        var data = await TaskViews.CardDataAsync(db, tasks, ct);
        var now = clock.GetUtcNow();

        var columns = new List<TaskColumnDto>(TaskViews.Columns.Length);
        foreach (var status in TaskViews.Columns)
        {
            var cards = new List<TaskCardDto>();
            foreach (var task in tasks.Where(t => t.Status == status))
                cards.Add(TaskViews.Card(task, data, now, await CanMoveAsync(scope, board, task, data, me.PersonId, ct), me.PersonId));
            columns.Add(new TaskColumnDto(status, cards.Count, cards));
        }
        return TypedResults.Ok(new TaskBoardDto(
            board || await scope.LeadsAnyCommitteeOfEventAsync(eventId, ct), chips, columns));
    }

    /// <summary><c>Tasks.Work</c> on one card, without a round trip per card: board, the committee's lead, or an assignee.</summary>
    private static async Task<bool> CanMoveAsync(
        ICommitteeContext scope, bool board, TaskItem task, TaskViews.CardData data, Guid? personId, CancellationToken ct)
    {
        if (task.IsArchived) return false;
        if (board) return true;
        if (await scope.IsLeadOfAsync(task.CommitteeId, ct)) return true;
        return personId is { } me && data.Assignees.TryGetValue(task.Id, out var list) && list.Any(a => a.PersonId == me);
    }

    private static async Task<IResult> Mine(HtnDbContext db, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (me.PersonId is not { } personId) return TypedResults.Ok(new MyTasksDto([], 0));
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsArchived && t.Status != TaskBoardStatus.Done
                        && db.TaskAssignees.Any(a => a.TaskId == t.Id && a.PersonId == personId))
            .ToListAsync(ct);
        // Soonest due first, undated last (frame 07's "My tasks" page).
        tasks = [.. tasks.OrderBy(t => t.DueAt is null).ThenBy(t => t.DueAt).ThenBy(t => t.CreatedAt)];
        var data = await TaskViews.CardDataAsync(db, tasks, ct);
        var now = clock.GetUtcNow();
        var eventIds = tasks.Select(t => t.EventId).Distinct().ToList();
        var events = await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.StartsAt }).ToListAsync(ct);

        var groups = events.OrderByDescending(e => e.StartsAt).Select(e => new MyTasksGroupDto(e.Id, e.Name,
            tasks.Where(t => t.EventId == e.Id)
                // The caller is an assignee of every task here, so they may always move it.
                .Select(t => TaskViews.Card(t, data, now, canMove: true, personId)).ToList())).ToList();
        return TypedResults.Ok(new MyTasksDto(groups, tasks.Count));
    }

    // ---------- Create ----------

    private static async Task<IResult> Create(
        Guid eventId, CreateTaskRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var committeeId = req.CommitteeId!.Value;
        // The reference number comes from the counter on the event row: the event row is locked first, always.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var ev = await db.Events.SingleAsync(e => e.Id == eventId, ct);
        var committee = await db.Committees.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == committeeId && c.EventId == eventId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.TasksManage) is { } denied) return denied;
        if (!committee.IsActive)
            return TaskProblems.Conflict("This committee is deactivated.", TaskErrorCodes.CommitteeInactive);

        var now = clock.GetUtcNow();
        var status = req.Status ?? TaskBoardStatus.Backlog;
        var task = TaskItem.Create(eventId, committeeId, ev.NextTaskNumber(), req.Title, req.Description, status,
            req.BasePoints ?? 0, req.DueAt, req.Labels, await TaskOps.NextSortOrderAsync(db, eventId, status, ct), now, me.Id);
        db.Tasks.Add(task);
        TaskOps.Log(db, task.Id, me.Id, TaskActivityKinds.Created, $"created this task in {TaskViews.Label(status)}", now);
        audit.Record(me.Id, "task.create", nameof(TaskItem), task.Id.ToString(), after: TaskOps.Audit(task));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.Created($"/api/tasks/{task.Id}",
            await TaskViews.DetailAsync(db, task, await TaskViews.FlagsAsync(auth, http, task), me.Id,
                http.User.IsInRole(Roles.Board), now, ct));
    }
}

internal static class TaskProblems
{
    public static IResult Conflict(string title, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, title, code: code);

    public static IResult Done() =>
        Conflict("This task is done. Reopen it first.", TaskErrorCodes.TaskDone);

    public static IResult Archived() =>
        Conflict("This task is archived.", TaskErrorCodes.TaskArchived);

    public static IResult NeedsApproval() =>
        Conflict("Only the committee lead or an admin can approve a task into Done.", TaskErrorCodes.NeedsApproval);

    public static IResult NoLead() =>
        Conflict("Give the task a Lead before approving it.", TaskErrorCodes.NoLead);
}
