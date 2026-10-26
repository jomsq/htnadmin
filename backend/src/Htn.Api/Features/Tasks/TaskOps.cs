using Htn.Domain.Settings;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// The write-side rules shared by the task endpoints and the dev seeder: the activity log, awarding points when a task
/// is approved into Done (once, in the caller's transaction) and reversing them when it is reopened.
/// </summary>
internal static class TaskOps
{
    /// <summary>Appends one line to the task's activity log (append-only, trigger-enforced).</summary>
    public static TaskActivity Log(HtnDbContext db, Guid taskId, Guid? actorId, string kind, string text, DateTimeOffset at)
    {
        var entry = TaskActivity.Record(taskId, actorId, kind, text, at);
        db.TaskActivities.Add(entry);
        return entry;
    }

    /// <summary>A task can only be approved with someone in the Lead role (409 <c>no_lead</c>).</summary>
    public static Task<bool> HasLeadAsync(HtnDbContext db, Guid taskId, CancellationToken ct) =>
        db.TaskAssignees.AnyAsync(a => a.TaskId == taskId && a.Role == TaskAssigneeRole.Lead, ct);

    /// <summary>The people holding the Lead role on this task (volunteers never lead).</summary>
    public static async Task<List<Guid>> LeadPersonIdsAsync(HtnDbContext db, Guid taskId, CancellationToken ct) =>
        await db.TaskAssignees.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.Role == TaskAssigneeRole.Lead && a.PersonId != null)
            .Select(a => a.PersonId!.Value).ToListAsync(ct);

    /// <summary>
    /// Nobody signs off their own work (user decision): approving into Done is refused when the approver is the
    /// <b>only</b> Lead on the card. A card with several Leads is fine even when the approver is one of them — the
    /// other Lead is the second pair of eyes. Any other lead of that committee, and every board member, can always
    /// approve, so a card is never stuck.
    /// </summary>
    public static bool IsSoleLead(IReadOnlyCollection<Guid> leadPersonIds, Guid? personId) =>
        personId is { } me && leadPersonIds.Count == 1 && leadPersonIds.Contains(me);

    /// <summary>True when this caller may not be the one to finish this task (see <see cref="IsSoleLead"/>).</summary>
    public static async Task<bool> NeedsOtherApproverAsync(HtnDbContext db, Guid taskId, Guid? personId, CancellationToken ct) =>
        personId is not null && IsSoleLead(await LeadPersonIdsAsync(db, taskId, ct), personId);

    public static Task<bool> HasLiveAwardsAsync(HtnDbContext db, Guid taskId, CancellationToken ct) =>
        db.PointsAwards.AnyAsync(a => a.TaskId == taskId && a.ReversedAt == null, ct);

    /// <summary>
    /// Writes one award per person assigned to the task, snapshotting the base points, the role and the multiplier in
    /// force right now. Volunteers are skipped: they never earn points. Idempotent — a task that already holds live
    /// awards gets none, so a second approve (or two at the same time) can't pay twice. The caller runs this inside the
    /// transaction that moves the task to Done, with the task row locked.
    /// </summary>
    public static async Task<List<PointsAward>> AwardAsync(
        HtnDbContext db, TaskItem task, OrgSettings settings, DateTimeOffset at, Guid? actorId, CancellationToken ct)
    {
        if (await HasLiveAwardsAsync(db, task.Id, ct)) return [];
        var assignees = (await db.TaskAssignees.AsNoTracking()
                .Where(a => a.TaskId == task.Id && a.PersonId != null).ToListAsync(ct))
            .OrderBy(a => a.Role).ThenBy(a => a.AddedAt).ToList();
        var awards = new List<PointsAward>();
        foreach (var assignee in assignees)
        {
            var award = PointsAward.Create(task, assignee.PersonId!.Value, assignee.Role,
                TaskViews.Multiplier(settings, assignee.Role), at, actorId);
            db.PointsAwards.Add(award);
            awards.Add(award);
        }
        return awards;
    }

    /// <summary>Reverses every live award on the task with a reason. Rows are stamped, never deleted.</summary>
    public static async Task<List<PointsAward>> ReverseAsync(
        HtnDbContext db, Guid taskId, string reason, DateTimeOffset at, Guid? actorId, CancellationToken ct)
    {
        var live = await db.PointsAwards.Where(a => a.TaskId == taskId && a.ReversedAt == null).ToListAsync(ct);
        foreach (var award in live) award.Reverse(reason, at, actorId);
        return live;
    }

    /// <summary>Appends to the bottom of a column (frame 07's "+ Add task"), leaving room between cards.</summary>
    public static async Task<int> NextSortOrderAsync(HtnDbContext db, Guid eventId, TaskBoardStatus status, CancellationToken ct)
    {
        var max = await db.Tasks.Where(t => t.EventId == eventId && t.Status == status).MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;
        return Math.Min(max + 100, TaskItem.MaxSortOrder);
    }

    /// <summary>
    /// Locks the task row until the current transaction ends, so approve, reopen and assignee changes never
    /// interleave. <c>FOR NO KEY UPDATE</c>, not <c>FOR UPDATE</c>: it still serializes the writers that matter, but
    /// it does not block the foreign keys pointing at the row, so an open approve never stalls a comment, checklist
    /// item, attachment or activity line being inserted on the same task.
    /// </summary>
    public static async Task LockAsync(HtnDbContext db, Guid taskId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Locking a task needs a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM tasks WHERE id = {taskId} FOR NO KEY UPDATE", ct);
    }

    public static string PointsText(IEnumerable<PointsAward> awards) =>
        decimal.Round(awards.Sum(a => a.Points), 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Audit snapshot of a task (money-adjacent: points, status and assignees are all audited).</summary>
    public static object Audit(TaskItem task) => new
    {
        task.EventId, task.CommitteeId, task.Reference, task.Title, Status = task.Status.ToString(), task.BasePoints,
        task.DueAt, task.Labels, task.IsArchived, task.ApprovedAt,
    };

    public static object Audit(PointsAward award) => new
    {
        award.TaskId, award.PersonId, Role = award.Role.ToString(), award.BasePoints, award.Multiplier, award.Points,
        award.AwardedAt, award.ReversedAt,
    };
}
