using Htn.Api.Common;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Htn.Api.Auth;

/// <summary>What a resource-scoped policy is asking about.</summary>
public enum CommitteeAccess
{
    /// <summary><c>Committees.ManageMembers</c>: board, or the lead of that committee.</summary>
    ManageMembers,

    /// <summary><c>Tasks.Manage</c>: board, or the lead of the task's (or the target) committee.</summary>
    TasksManage,

    /// <summary><c>Tasks.Work</c>: board, the committee lead, or someone assigned to the task.</summary>
    TasksWork,

    /// <summary><c>Tasks.ViewBoard</c>: board, or anyone on any committee of that event.</summary>
    ViewBoard,
}

public sealed class CommitteeScopedRequirement(CommitteeAccess access) : IAuthorizationRequirement
{
    public CommitteeAccess Access { get; } = access;
}

/// <summary>The authorization resource for a committee-scoped check.</summary>
public sealed record CommitteeScope(Guid CommitteeId);

/// <summary>The authorization resource for an event-scoped check (the task board of one event).</summary>
public sealed record EventScope(Guid EventId);

/// <summary>The authorization resource for a task-scoped check. Resolved by <see cref="ITaskScopeResolver"/> (slice 4B).</summary>
public sealed record TaskScope(Guid TaskId);

/// <summary>Where a task sits and whether the caller is assigned to it.</summary>
public sealed record TaskScopeInfo(Guid TaskId, Guid CommitteeId, Guid EventId, bool IsCallerAssignee);

/// <summary>
/// Maps a task id to its committee, event and the caller's assignment. Implemented by
/// <c>Features.Tasks.TaskScopeResolver</c> over the <c>tasks</c> / <c>task_assignees</c> tables (slice 4B); an unknown
/// task resolves to null, so a <see cref="TaskScope"/> check fails closed.
/// </summary>
public interface ITaskScopeResolver
{
    Task<TaskScopeInfo?> ResolveAsync(Guid taskId, CancellationToken ct = default);
}

/// <summary>
/// The one handler behind the four resource-scoped policies. Board members are admins everywhere and always succeed.
/// Otherwise the decision comes from <see cref="ICommitteeContext"/>:
/// <list type="bullet">
/// <item><see cref="CommitteeScope"/> — the lead of that committee (manage) or anyone on it (work/view).</item>
/// <item><see cref="EventScope"/> — a lead on that event (manage) or anyone on a committee of it (work/view).</item>
/// <item><see cref="TaskScope"/> — resolved through <see cref="ITaskScopeResolver"/>: the lead of the task's
/// committee (manage), an assignee or that lead (work), anyone on a committee of its event (view).</item>
/// <item><b>Anything else</b> — no resource (<c>/api/auth/me</c>) or the <c>HttpContext</c> that endpoint routing
/// passes for <c>.RequireAuthorization(...)</c>: <b>"may be allowed"</b>, i.e. the caller leads something (manage) or
/// is on something (work/view). That is the coarse gate; the endpoint must then check the real resource, and the
/// per-resource <c>canManage</c> flags in the payloads are what the UI uses.</item>
/// </list>
/// </summary>
public sealed class CommitteeScopedHandler(ICommitteeContext committees, ITaskScopeResolver tasks)
    : AuthorizationHandler<CommitteeScopedRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CommitteeScopedRequirement requirement)
    {
        if (context.User.IsInRole(Roles.Board))
        {
            context.Succeed(requirement);
            return;
        }
        var manage = requirement.Access is CommitteeAccess.ManageMembers or CommitteeAccess.TasksManage;
        var granted = context.Resource switch
        {
            CommitteeScope c => manage
                ? await committees.IsLeadOfAsync(c.CommitteeId)
                : await OnCommitteeOrItsEventAsync(c.CommitteeId, requirement.Access),
            EventScope e => manage
                ? await committees.LeadsAnyCommitteeOfEventAsync(e.EventId)
                : await committees.IsOnAnyCommitteeOfEventAsync(e.EventId),
            TaskScope t => await TaskAsync(t.TaskId, requirement.Access),
            // No resource of ours: the route-level check (where ASP.NET Core passes the HttpContext) and
            // /api/auth/me. Both mean "may be allowed" — see the class summary.
            _ => manage ? await committees.LeadsAnythingAsync() : await committees.IsOnAnythingAsync(),
        };
        if (granted) context.Succeed(requirement);
    }

    /// <summary>
    /// Work on a committee's cards needs to be on that committee; viewing its board is event-wide, so a member of a
    /// sibling committee of the same event also sees it.
    /// </summary>
    private async Task<bool> OnCommitteeOrItsEventAsync(Guid committeeId, CommitteeAccess access)
    {
        if (await committees.IsOnAsync(committeeId)) return true;
        if (access != CommitteeAccess.ViewBoard) return false;
        return await committees.EventOfAsync(committeeId) is { } eventId
               && await committees.IsOnAnyCommitteeOfEventAsync(eventId);
    }

    private async Task<bool> TaskAsync(Guid taskId, CommitteeAccess access)
    {
        if (await tasks.ResolveAsync(taskId) is not { } task) return false;
        return access switch
        {
            CommitteeAccess.ManageMembers or CommitteeAccess.TasksManage => await committees.IsLeadOfAsync(task.CommitteeId),
            CommitteeAccess.TasksWork => task.IsCallerAssignee || await committees.IsLeadOfAsync(task.CommitteeId),
            _ => await committees.IsOnAnyCommitteeOfEventAsync(task.EventId),
        };
    }
}

/// <summary>
/// Helper for endpoints whose scope can't be expressed in the route policy (the committee or event only becomes known
/// once the row is loaded). The route still carries <c>.RequireAuthorization(Policies.X)</c> — which keeps anyone who
/// can never hold the right out — and the handler then runs the same policy against the real resource and returns 403.
/// </summary>
public static class ScopedAuthorization
{
    public const string ForbiddenCode = "forbidden";

    public static async Task<bool> AllowsAsync(this IAuthorizationService auth, HttpContext http, object? resource, string policy) =>
        (await auth.AuthorizeAsync(http.User, resource, policy)).Succeeded;

    /// <summary>Null when allowed, otherwise the 403 to return.</summary>
    public static async Task<IResult?> DenyAsync(this IAuthorizationService auth, HttpContext http, object? resource, string policy) =>
        await auth.AllowsAsync(http, resource, policy)
            ? null
            : Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                "Only the board and the committee lead can change this committee.", ForbiddenCode);
}
