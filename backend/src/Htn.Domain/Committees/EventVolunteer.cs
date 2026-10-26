using Htn.Domain.Common;
using Htn.Domain.Registrations;

namespace Htn.Domain.Committees;

/// <summary>
/// Someone who helps at one event (frame 06, "+ Add volunteer (no login)"). No login, never a user, never on the
/// leaderboard and never awarded points. Optionally attached to one committee of the same event. Names follow the same
/// character rules as attendee names (<see cref="PersonNameRules"/>); contact details are optional and only used to
/// reach them about the event.
/// </summary>
public sealed class EventVolunteer
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = RegistrationForm.EmailMaxLength;
    public const int ReasonMaxLength = 500;

    private EventVolunteer() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Email { get; private set; }

    /// <summary>Stored normalized as <c>+639XXXXXXXXX</c>.</summary>
    public string? Mobile { get; private set; }

    public Guid? CommitteeId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static EventVolunteer Create(Guid eventId, string name, string? email, string? mobile, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EventId = eventId != Guid.Empty ? eventId : throw new DomainException("An event is required."),
        Name = Name_(name),
        Email = Email_(email),
        Mobile = Mobile_(mobile),
        IsActive = true,
        CreatedAt = at,
    };

    /// <summary>Returns true when anything changed.</summary>
    public bool Update(string name, string? email, string? mobile)
    {
        EnsureActive();
        var newName = Name_(name);
        var newEmail = Email_(email);
        var newMobile = Mobile_(mobile);
        if (newName == Name && newEmail == Email && newMobile == Mobile) return false;
        Name = newName;
        Email = newEmail;
        Mobile = newMobile;
        return true;
    }

    /// <summary>Attaches to a committee of the same event, or detaches when null. Returns true when it changed.</summary>
    public bool AttachTo(Committee? committee)
    {
        EnsureActive();
        if (committee is not null)
        {
            if (committee.EventId != EventId) throw new DomainException("That committee belongs to another event.");
            if (!committee.IsActive) throw new DomainException("This committee is deactivated.");
        }
        var id = committee?.Id;
        if (id == CommitteeId) return false;
        CommitteeId = id;
        return true;
    }

    /// <summary>Deactivates the volunteer and takes them off their committee. Never deleted.</summary>
    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This volunteer is already deactivated.");
        IsActive = false;
        DeactivatedAt = at;
        CommitteeId = null;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This volunteer is deactivated.");
    }

    private static string Name_(string? value)
    {
        var name = Committee.Required(value, "Name", NameMaxLength);
        if (!PersonNameRules.IsValidName(name)) throw new DomainException(PersonNameRules.Message);
        return name;
    }

    private static string? Email_(string? value)
    {
        var email = Committee.Optional(value, "Email", EmailMaxLength);
        if (email is null) return null;
        if (!RegistrationForm.IsEmail(email)) throw new DomainException("Enter a valid email address.");
        return email.ToLowerInvariant();
    }

    private static string? Mobile_(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return MobileNumber.Normalize(value) ?? throw new DomainException("Enter a Philippine mobile number, e.g. 0917 123 4567.");
    }
}
