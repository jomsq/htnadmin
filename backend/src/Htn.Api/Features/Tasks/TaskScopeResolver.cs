using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Committees;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// Slice 4B's real <see cref="ITaskScopeResolver"/>: maps a task id to its committee, its event and whether the caller
/// is one of its assignees, which is all <c>CommitteeScopedHandler</c> needs to decide <c>Tasks.Manage</c>,
/// <c>Tasks.Work</c> and <c>Tasks.ViewBoard</c> on a task. Answers are cached per request (the handler is asked the
/// same question several times while a payload's permission flags are built), and an unknown task resolves to null, so
/// the check fails closed exactly as it did in 4A.
/// </summary>
public sealed class TaskScopeResolver(HtnDbContext db, ICurrentUser me) : ITaskScopeResolver
{
    private readonly Dictionary<Guid, TaskScopeInfo?> _cache = [];

    public async Task<TaskScopeInfo?> ResolveAsync(Guid taskId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(taskId, out var cached)) return cached;
        var personId = me.PersonId;
        var row = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => new
            {
                t.CommitteeId,
                t.EventId,
                IsAssignee = personId != null && db.TaskAssignees.Any(a => a.TaskId == t.Id && a.PersonId == personId),
            })
            .SingleOrDefaultAsync(ct);
        return _cache[taskId] = row is null ? null : new TaskScopeInfo(taskId, row.CommitteeId, row.EventId, row.IsAssignee);
    }

    public void Invalidate(Guid taskId) => _cache.Remove(taskId);
}

/// <summary>
/// Slice 4B's real <see cref="ICommitteeOpenTasks"/>: open means on the board (not archived) and not yet Done. The
/// counts show on every committee card and gate deactivation — a committee is only closed once its board is clear.
/// </summary>
public sealed class CommitteeOpenTasks(HtnDbContext db) : ICommitteeOpenTasks
{
    public async Task<IReadOnlyDictionary<Guid, int>> ForEventAsync(Guid eventId, CancellationToken ct = default) =>
        await db.Tasks.AsNoTracking()
            .Where(t => t.EventId == eventId && !t.IsArchived && t.Status != TaskBoardStatus.Done)
            .GroupBy(t => t.CommitteeId)
            .Select(g => new { CommitteeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CommitteeId, g => g.Count, ct);

    public Task<int> ForCommitteeAsync(Guid committeeId, CancellationToken ct = default) =>
        db.Tasks.AsNoTracking()
            .CountAsync(t => t.CommitteeId == committeeId && !t.IsArchived && t.Status != TaskBoardStatus.Done, ct);
}
