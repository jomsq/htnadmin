namespace Htn.Api.Features.Committees;

/// <summary>
/// Open (not Done, not archived) task counts per committee. Shown on every committee card and used by the deactivate
/// guard: a committee is only deactivated once its board is clear.
/// Implemented by <c>Features.Tasks.CommitteeOpenTasks</c> (slice 4B) as one grouped query over the <c>tasks</c>
/// table: not archived and not Done.
/// </summary>
public interface ICommitteeOpenTasks
{
    /// <summary>Open task counts by committee id for one event. A missing key means 0.</summary>
    Task<IReadOnlyDictionary<Guid, int>> ForEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Open tasks of one committee.</summary>
    Task<int> ForCommitteeAsync(Guid committeeId, CancellationToken ct = default);
}
