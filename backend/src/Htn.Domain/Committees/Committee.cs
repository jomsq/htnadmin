using Htn.Domain.Common;

namespace Htn.Domain.Committees;

/// <summary>
/// A per-event team (frame 06): Logistics, Programs, Partnerships, Marketing, Registration &amp; Check-in, Tech &amp; AV…
/// One lead (always an active goon, checked by the caller against the roster) plus goons, apprentices and volunteers.
/// Names are unique per event, case-insensitively (<c>ux_committees_event_name</c>). A committee is never deleted:
/// it is deactivated, and only while it holds no open tasks.
/// </summary>
public sealed class Committee
{
    public const int NameMaxLength = 60;
    public const int MaxSortOrder = 1000;
    public const int ReasonMaxLength = 500;

    private Committee() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>The lead, who may assign and score this committee's tasks. Mirrored by a <c>Lead</c> member row.</summary>
    public Guid? LeadPersonId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static Committee Create(Guid eventId, string name, int sortOrder, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EventId = eventId != Guid.Empty ? eventId : throw new DomainException("An event is required."),
        Name = Required(name, "Name", NameMaxLength),
        SortOrder = Order(sortOrder),
        IsActive = true,
        CreatedAt = at,
    };

    /// <summary>Renames and reorders. Returns true when anything changed.</summary>
    public bool Update(string name, int sortOrder)
    {
        EnsureActive();
        var newName = Required(name, "Name", NameMaxLength);
        var newOrder = Order(sortOrder);
        if (newName == Name && newOrder == SortOrder) return false;
        Name = newName;
        SortOrder = newOrder;
        return true;
    }

    /// <summary>
    /// Points the committee at its lead (or clears it). The caller has already checked that the person is an active
    /// goon and is not already on another committee of this event, and keeps the <c>Lead</c> member row in step.
    /// </summary>
    public void SetLead(Guid? personId)
    {
        EnsureActive();
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        LeadPersonId = personId;
    }

    /// <summary>Deactivates the committee. The caller has already checked that it holds no open tasks.</summary>
    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This committee is already deactivated.");
        IsActive = false;
        DeactivatedAt = at;
        LeadPersonId = null;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This committee is deactivated.");
    }

    private static int Order(int sortOrder) =>
        sortOrder is >= 0 and <= MaxSortOrder ? sortOrder : throw new DomainException($"Sort order must be between 0 and {MaxSortOrder}.");

    internal static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    internal static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}

public enum CommitteeRole
{
    Lead,
    Member,
}
