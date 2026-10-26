using Htn.Domain.Common;

namespace Htn.Domain.Organization;

/// <summary>Anyone on the roster: board, goons, apprentices and members.</summary>
public sealed class Person
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PositionMaxLength = 120;
    public const int ReasonMaxLength = 500;

    private readonly List<PersonHistory> _history = [];

    private Person() { }

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; } = default!;

    /// <summary>Stored trimmed and lower-cased so uniqueness is case-insensitive.</summary>
    public string Email { get; private set; } = default!;

    public PersonLevel Level { get; private set; }
    public string? Position { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public IReadOnlyList<PersonHistory> History => _history;

    public string FullName => MiddleName is null ? $"{FirstName} {LastName}" : $"{FirstName} {MiddleName} {LastName}";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static Person Create(
        string firstName, string? middleName, string lastName, string email,
        PersonLevel level, string? position, DateTimeOffset at, Guid? actorId)
    {
        var person = new Person
        {
            Id = Guid.CreateVersion7(at),
            FirstName = Required(firstName, nameof(FirstName), NameMaxLength),
            MiddleName = Optional(middleName, nameof(MiddleName), NameMaxLength),
            LastName = Required(lastName, nameof(LastName), NameMaxLength),
            Email = NormalizeEmail(Required(email, nameof(Email), EmailMaxLength)),
            Level = Enum.IsDefined(level) ? level : throw new DomainException("Unknown level."),
            Position = Optional(position, nameof(Position), PositionMaxLength),
            IsActive = true,
            CreatedAt = at,
        };
        person._history.Add(new PersonHistory(person.Id, at, actorId, PersonHistoryKinds.Created, null, level.ToString(), null));
        return person;
    }

    public void ChangeLevel(PersonLevel newLevel, DateTimeOffset at, Guid? actorId, string? reason)
    {
        EnsureActive();
        if (!Enum.IsDefined(newLevel)) throw new DomainException("Unknown level.");
        if (newLevel == Level) throw new DomainException($"This person is already {newLevel}.");
        var from = Level;
        Level = newLevel;
        _history.Add(new PersonHistory(Id, at, actorId, PersonHistoryKinds.LevelChanged, from.ToString(), newLevel.ToString(), reason));
    }

    /// <summary>Why <see cref="Promote"/> would refuse a move, or <see cref="PromotionError.None"/>.</summary>
    public PromotionError CheckPromotion(PersonLevel to, string? reason)
    {
        if (!IsActive) return PromotionError.Inactive;
        if (!Enum.IsDefined(to)) return PromotionError.UnknownLevel;
        return PromotionRules.Classify(Level, to) switch
        {
            LevelChangeKind.Same => PromotionError.SameLevel,
            LevelChangeKind.SkipsAStep => PromotionError.SkipsAStep,
            LevelChangeKind.Demotion when string.IsNullOrWhiteSpace(reason) => PromotionError.ReasonRequired,
            _ => PromotionError.None,
        };
    }

    /// <summary>
    /// Moves the person along the level path. Upward: Member → Apprentice → Goon one step at a time, or any level → Board.
    /// Any move down is a demotion and needs a reason. Writes a LevelChanged history entry.
    /// </summary>
    public LevelChangeKind Promote(PersonLevel to, DateTimeOffset at, Guid? actorId, string? reason)
    {
        var error = CheckPromotion(to, reason);
        if (error != PromotionError.None) throw new DomainException(PromotionErrors.Message(error, Level, to));
        var kind = PromotionRules.Classify(Level, to);
        ChangeLevel(to, at, actorId, Optional(reason, "Reason", ReasonMaxLength));
        return kind;
    }

    /// <summary>Updates the name fields. Returns true if anything changed.</summary>
    public bool Rename(string firstName, string? middleName, string lastName)
    {
        EnsureActive();
        var first = Required(firstName, nameof(FirstName), NameMaxLength);
        var middle = Optional(middleName, nameof(MiddleName), NameMaxLength);
        var last = Required(lastName, nameof(LastName), NameMaxLength);
        if (first == FirstName && middle == MiddleName && last == LastName) return false;
        FirstName = first;
        MiddleName = middle;
        LastName = last;
        return true;
    }

    /// <summary>
    /// Changes the roster email. The caller decides whether that's allowed (only while there is no login or the invite
    /// is still pending). Returns true if it changed.
    /// </summary>
    public bool ChangeEmail(string email)
    {
        EnsureActive();
        var normalized = NormalizeEmail(Required(email, nameof(Email), EmailMaxLength));
        if (normalized == Email) return false;
        Email = normalized;
        return true;
    }

    internal void OnPositionAssigned(string title, DateTimeOffset at, Guid? actorId)
    {
        EnsureActive();
        Position = title;
        _history.Add(new PersonHistory(Id, at, actorId, PersonHistoryKinds.PositionAssigned, null, title, null));
    }

    internal void OnPositionVacated(string title, DateTimeOffset at, Guid? actorId, string? reason)
    {
        if (Position == title) Position = null;
        _history.Add(new PersonHistory(Id, at, actorId, PersonHistoryKinds.PositionVacated, title, null, reason));
    }

    public void ChangePosition(string? position, DateTimeOffset at, Guid? actorId)
    {
        EnsureActive();
        var normalized = Optional(position, nameof(Position), PositionMaxLength);
        if (normalized == Position) return;
        var from = Position;
        Position = normalized;
        _history.Add(new PersonHistory(Id, at, actorId, PersonHistoryKinds.PositionChanged, from, normalized, null));
    }

    public void Deactivate(DateTimeOffset at, Guid? actorId, string? reason)
    {
        if (!IsActive) throw new DomainException("This person is already deactivated.");
        var normalizedReason = Optional(reason, "Reason", ReasonMaxLength);
        IsActive = false;
        DeactivatedAt = at;
        _history.Add(new PersonHistory(Id, at, actorId, PersonHistoryKinds.Deactivated, "active", "deactivated", normalizedReason));
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This person is deactivated.");
    }

    private static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException($"{field} is required.");
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    private static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
