namespace Htn.Api.Features.Committees;

/// <summary>
/// Open (not Done, not archived) task counts per committee. Shown on every committee card and used by the deactivate
/// guard: a committee is only deactivated once its board is clear.
/// Slice 4A has no tasks, so <see cref="NoTasksYetOpenTasks"/> answers 0 for everything; slice 4B swaps in a query
/// over the <c>tasks</c> table and this feature needs no other change.
/// </summary>
public interface ICommitteeOpenTasks
{
    /// <summary>Open task counts by committee id for one event. A missing key means 0.</summary>
    Task<IReadOnlyDictionary<Guid, int>> ForEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Open tasks of one committee.</summary>
    Task<int> ForCommitteeAsync(Guid committeeId, CancellationToken ct = default);
}

/// <summary>Slice 4A placeholder. TODO(4B): replace the registration with a query over the task table.</summary>
public sealed class NoTasksYetOpenTasks : ICommitteeOpenTasks
{
    private static readonly IReadOnlyDictionary<Guid, int> Empty = new Dictionary<Guid, int>();

    public Task<IReadOnlyDictionary<Guid, int>> ForEventAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(Empty);

    public Task<int> ForCommitteeAsync(Guid committeeId, CancellationToken ct = default) => Task.FromResult(0);
}
