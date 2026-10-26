using Htn.Api.Common;
using Htn.Domain.Committees;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Auth;

/// <summary>One active committee membership of the caller.</summary>
public sealed record CommitteeMembership(Guid CommitteeId, Guid EventId, CommitteeRole Role);

/// <summary>
/// The caller's committee memberships, loaded once per request. Every resource-scoped authorization decision
/// (<c>Committees.ManageMembers</c>, <c>Tasks.Manage</c>, <c>Tasks.Work</c>, <c>Tasks.ViewBoard</c>) and every
/// permission flag in a payload goes through this service, so a request never re-queries the same facts.
/// Only active rows of active committees count: leaving a committee or deactivating it takes the rights away.
/// </summary>
public interface ICommitteeContext
{
    Task<IReadOnlyList<CommitteeMembership>> MembershipsAsync(CancellationToken ct = default);

    /// <summary>The caller is the lead of this committee.</summary>
    Task<bool> IsLeadOfAsync(Guid committeeId, CancellationToken ct = default);

    /// <summary>The caller is the lead or a member of this committee.</summary>
    Task<bool> IsOnAsync(Guid committeeId, CancellationToken ct = default);

    /// <summary>The caller is on any committee of this event (what <c>Tasks.ViewBoard</c> means).</summary>
    Task<bool> IsOnAnyCommitteeOfEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>The caller leads any committee of this event.</summary>
    Task<bool> LeadsAnyCommitteeOfEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>The caller leads at least one committee anywhere ("may be allowed", for the resource-less check).</summary>
    Task<bool> LeadsAnythingAsync(CancellationToken ct = default);

    /// <summary>The caller is on at least one committee anywhere ("may be allowed", for the resource-less check).</summary>
    Task<bool> IsOnAnythingAsync(CancellationToken ct = default);

    /// <summary>The event a committee belongs to, or null when it doesn't exist. Cached per request.</summary>
    Task<Guid?> EventOfAsync(Guid committeeId, CancellationToken ct = default);
}

public sealed class CommitteeContext(HtnDbContext db, ICurrentUser me) : ICommitteeContext
{
    private readonly Dictionary<Guid, Guid?> _eventOfCommittee = [];
    private Task<IReadOnlyList<CommitteeMembership>>? _memberships;

    public Task<IReadOnlyList<CommitteeMembership>> MembershipsAsync(CancellationToken ct = default) =>
        _memberships ??= LoadAsync(ct);

    private async Task<IReadOnlyList<CommitteeMembership>> LoadAsync(CancellationToken ct)
    {
        if (me.PersonId is not { } personId) return [];
        var rows = await db.CommitteeMembers.AsNoTracking()
            .Where(m => m.PersonId == personId && m.LeftAt == null)
            .Join(db.Committees.AsNoTracking().Where(c => c.IsActive), m => m.CommitteeId, c => c.Id,
                (m, c) => new CommitteeMembership(m.CommitteeId, m.EventId, m.Role))
            .ToListAsync(ct);
        foreach (var row in rows) _eventOfCommittee[row.CommitteeId] = row.EventId;
        return rows;
    }

    public async Task<bool> IsLeadOfAsync(Guid committeeId, CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Any(m => m.CommitteeId == committeeId && m.Role == CommitteeRole.Lead);

    public async Task<bool> IsOnAsync(Guid committeeId, CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Any(m => m.CommitteeId == committeeId);

    public async Task<bool> IsOnAnyCommitteeOfEventAsync(Guid eventId, CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Any(m => m.EventId == eventId);

    public async Task<bool> LeadsAnyCommitteeOfEventAsync(Guid eventId, CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Any(m => m.EventId == eventId && m.Role == CommitteeRole.Lead);

    public async Task<bool> LeadsAnythingAsync(CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Any(m => m.Role == CommitteeRole.Lead);

    public async Task<bool> IsOnAnythingAsync(CancellationToken ct = default) =>
        (await MembershipsAsync(ct)).Count > 0;

    public async Task<Guid?> EventOfAsync(Guid committeeId, CancellationToken ct = default)
    {
        await MembershipsAsync(ct);
        if (_eventOfCommittee.TryGetValue(committeeId, out var cached)) return cached;
        var eventId = await db.Committees.AsNoTracking().Where(c => c.Id == committeeId)
            .Select(c => (Guid?)c.EventId).SingleOrDefaultAsync(ct);
        return _eventOfCommittee[committeeId] = eventId;
    }
}
