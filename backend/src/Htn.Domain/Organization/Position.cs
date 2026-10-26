using Htn.Domain.Common;

namespace Htn.Domain.Organization;

public enum AssignError
{
    None,
    PositionInactive,
    PersonInactive,
    WrongLevel,
    AlreadyHoldsBoardSeat,
    AlreadyHolder,
}

/// <summary>A board seat or goon position. It has at most one current holder; past holders live in <see cref="PositionAssignment"/>.</summary>
public sealed class Position
{
    public const int TitleMaxLength = 120;
    public const int ReasonMaxLength = 500;

    private Position() { }

    public Guid Id { get; private set; }
    public PositionKind Kind { get; private set; }
    public string Title { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? HolderId { get; private set; }
    public DateTimeOffset? HolderSince { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Position Create(PositionKind kind, string title, int sortOrder, DateTimeOffset at)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Unknown position kind.");
        return new Position
        {
            Id = Guid.CreateVersion7(at),
            Kind = kind,
            Title = RequiredTitle(title),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = at,
        };
    }

    public void Rename(string title) => Title = RequiredTitle(title);

    public void SetActive(bool active)
    {
        if (active == IsActive) return;
        if (!active && HolderId is not null) throw new DomainException("Vacate the position before deactivating it.");
        IsActive = active;
    }

    /// <param name="holdsAnotherBoardSeat">Whether the person already holds a different board seat (a person holds at most one).</param>
    public AssignError CheckAssign(Person person, bool holdsAnotherBoardSeat)
    {
        if (!IsActive) return AssignError.PositionInactive;
        if (!person.IsActive) return AssignError.PersonInactive;
        if (HolderId == person.Id) return AssignError.AlreadyHolder;
        if (person.Level != PromotionRules.RequiredLevel(Kind)) return AssignError.WrongLevel;
        if (Kind == PositionKind.BoardSeat && holdsAnotherBoardSeat) return AssignError.AlreadyHoldsBoardSeat;
        return AssignError.None;
    }

    /// <summary>
    /// Makes <paramref name="person"/> the holder. The position must be vacant (vacate the current holder first).
    /// Writes a PositionAssigned history entry on the person and returns the new assignment record.
    /// </summary>
    public PositionAssignment Assign(Person person, bool holdsAnotherBoardSeat, DateTimeOffset at, Guid? actorId)
    {
        var error = CheckAssign(person, holdsAnotherBoardSeat);
        if (error != AssignError.None) throw new DomainException(AssignErrorMessage(error));
        if (HolderId is not null) throw new DomainException("This position is held. Vacate it first.");
        HolderId = person.Id;
        HolderSince = at;
        person.OnPositionAssigned(Title, at, actorId);
        return PositionAssignment.Start(Id, person.Id, at, actorId);
    }

    /// <summary>Ends the current holder's term. Writes a PositionVacated history entry on the holder.</summary>
    public void Vacate(Person holder, PositionAssignment current, DateTimeOffset at, Guid? actorId, string? reason)
    {
        if (HolderId is null) throw new DomainException("This position is already vacant.");
        if (holder.Id != HolderId || current.PersonId != HolderId || current.PositionId != Id || current.EndedAt is not null)
            throw new DomainException("That isn't the current holder of this position.");
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalized is { Length: > ReasonMaxLength }) throw new DomainException($"Reason must be at most {ReasonMaxLength} characters.");
        current.End(at, actorId, normalized);
        holder.OnPositionVacated(Title, at, actorId, normalized);
        HolderId = null;
        HolderSince = null;
    }

    public static string AssignErrorMessage(AssignError error) => error switch
    {
        AssignError.PositionInactive => "This position is inactive.",
        AssignError.PersonInactive => "This person is deactivated.",
        AssignError.WrongLevel => "Board seats are held by Board members and goon positions by Goons.",
        AssignError.AlreadyHoldsBoardSeat => "This person already holds a board seat.",
        AssignError.AlreadyHolder => "This person already holds this position.",
        _ => "",
    };

    private static string RequiredTitle(string? title)
    {
        var v = title?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("Title is required.");
        if (v.Length > TitleMaxLength) throw new DomainException($"Title must be at most {TitleMaxLength} characters.");
        return v;
    }
}

/// <summary>One holder's term in a position (who, from, to). Kept forever; only the end is ever written later.</summary>
public sealed class PositionAssignment
{
    private PositionAssignment() { }

    public Guid Id { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public Guid? AssignedById { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public Guid? EndedById { get; private set; }
    public string? EndReason { get; private set; }

    internal static PositionAssignment Start(Guid positionId, Guid personId, DateTimeOffset at, Guid? actorId) => new()
    {
        Id = Guid.CreateVersion7(at),
        PositionId = positionId,
        PersonId = personId,
        StartedAt = at,
        AssignedById = actorId,
    };

    internal void End(DateTimeOffset at, Guid? actorId, string? reason)
    {
        if (EndedAt is not null) throw new DomainException("This term has already ended.");
        EndedAt = at < StartedAt ? StartedAt : at;
        EndedById = actorId;
        EndReason = reason;
    }
}
