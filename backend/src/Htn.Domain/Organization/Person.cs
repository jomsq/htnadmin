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
