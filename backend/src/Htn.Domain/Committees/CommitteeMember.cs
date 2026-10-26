using Htn.Domain.Common;

namespace Htn.Domain.Committees;

/// <summary>
/// Someone with a login (board, goon or apprentice) on a committee. Joining and leaving are history, so a row is never
/// deleted: leaving stamps <see cref="LeftAt"/>. A person is on at most one committee per event while active, kept by
/// the partial unique index <c>ux_committee_members_active_person</c> on (event_id, person_id) WHERE left_at IS NULL;
/// <c>ux_committee_members_active_lead</c> keeps at most one active Lead row per committee.
/// <see cref="EventId"/> is denormalized from the committee so both indexes and the composite foreign key work.
/// </summary>
public sealed class CommitteeMember
{
    private CommitteeMember() { }

    public Guid Id { get; private set; }
    public Guid CommitteeId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public CommitteeRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public bool IsActive => LeftAt is null;

    public static CommitteeMember Join(Committee committee, Guid personId, CommitteeRole role, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(committee);
        if (!committee.IsActive) throw new DomainException("This committee is deactivated.");
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        if (!Enum.IsDefined(role)) throw new DomainException("Unknown committee role.");
        return new CommitteeMember
        {
            Id = Guid.CreateVersion7(at),
            CommitteeId = committee.Id,
            EventId = committee.EventId,
            PersonId = personId,
            Role = role,
            JoinedAt = at,
        };
    }

    /// <summary>Promotes to lead or demotes to a plain member. Returns true when the role changed.</summary>
    public bool ChangeRole(CommitteeRole role)
    {
        EnsureActive();
        if (!Enum.IsDefined(role)) throw new DomainException("Unknown committee role.");
        if (role == Role) return false;
        Role = role;
        return true;
    }

    /// <summary>Records the person leaving. The row stays as history.</summary>
    public void Leave(DateTimeOffset at)
    {
        EnsureActive();
        if (at < JoinedAt) throw new DomainException("Someone can't leave before they joined.");
        LeftAt = at;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This person has already left the committee.");
    }
}
