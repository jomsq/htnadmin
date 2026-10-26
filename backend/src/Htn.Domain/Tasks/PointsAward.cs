using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// Points earned on one task by one person, written when the task is approved into Done. It is a <b>snapshot</b>: the
/// base points, the role and the multiplier used are stored on the row, so changing the multipliers in Settings never
/// rewrites history. Append-only like finance: reopening a task stamps <see cref="ReversedAt"/> with a reason instead
/// of deleting the row, and the leaderboard counts only rows where <see cref="ReversedAt"/> is null.
/// Volunteers never get a row.
/// </summary>
public sealed class PointsAward
{
    public const int ReasonMaxLength = 500;

    private PointsAward() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CommitteeId { get; private set; }
    public Guid PersonId { get; private set; }
    public TaskAssigneeRole Role { get; private set; }
    public int BasePoints { get; private set; }

    /// <summary>The role multiplier at the moment of the award (<c>numeric(4,2)</c>).</summary>
    public decimal Multiplier { get; private set; }

    /// <summary><see cref="BasePoints"/> × <see cref="Multiplier"/>, rounded to 2 decimals (<c>numeric(8,2)</c>).</summary>
    public decimal Points { get; private set; }

    public DateTimeOffset AwardedAt { get; private set; }
    public Guid? AwardedById { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }
    public Guid? ReversedById { get; private set; }
    public string? ReversalReason { get; private set; }

    public bool IsLive => ReversedAt is null;

    public static PointsAward Create(
        TaskItem task, Guid personId, TaskAssigneeRole role, decimal multiplier, DateTimeOffset at, Guid? actorId)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        if (!Enum.IsDefined(role)) throw new DomainException("Unknown task role.");
        if (multiplier < 0m || multiplier > 2m) throw new DomainException("Multipliers go from 0 to 2.");
        return new PointsAward
        {
            Id = Guid.CreateVersion7(at),
            TaskId = task.Id,
            EventId = task.EventId,
            CommitteeId = task.CommitteeId,
            PersonId = personId,
            Role = role,
            BasePoints = task.BasePoints,
            Multiplier = multiplier,
            Points = decimal.Round(task.BasePoints * multiplier, 2, MidpointRounding.AwayFromZero),
            AwardedAt = at,
            AwardedById = actorId,
        };
    }

    /// <summary>Offsets the award when the task leaves Done. A reason is always required; the row is never deleted.</summary>
    public void Reverse(string reason, DateTimeOffset at, Guid? actorId)
    {
        if (!IsLive) throw new DomainException("This award was already reversed.");
        ReversalReason = TaskRules.Required(reason, "Reason", ReasonMaxLength);
        ReversedAt = at;
        ReversedById = actorId;
    }
}
