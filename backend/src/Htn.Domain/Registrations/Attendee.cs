using Htn.Domain.Common;

namespace Htn.Domain.Registrations;

/// <summary>The form fields of one person on a checkout, already trimmed. Mobile is normalized to +63 by the caller.</summary>
public sealed record AttendeeDetails(
    string FirstName, string? MiddleName, string LastName, string? Gender, string Email, string? Mobile,
    string? Occupation, string? Company, string? ShirtSize, string? HearAbout);

/// <summary>
/// One seat of a registration (a group checkout has one per member; <see cref="SortOrder"/> 0 is the buyer).
/// <see cref="ParticipantId"/> is random and unique per event. <see cref="TicketVersion"/> is part of the signed QR
/// token: bumping it (payment reverted, cancelled, re-issued) makes every older QR invalid. Never deleted.
/// </summary>
public sealed class Attendee
{
    private Attendee() { }

    public Guid Id { get; private set; }
    public Guid RegistrationId { get; private set; }

    /// <summary>Copied from the registration so participant IDs can be unique per event in the database.</summary>
    public Guid EventId { get; private set; }

    public int SortOrder { get; private set; }
    public string ParticipantId { get; private set; } = default!;
    public string FirstName { get; private set; } = default!;
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; } = default!;
    public string? Gender { get; private set; }
    public string Email { get; private set; } = default!;
    public string? Mobile { get; private set; }
    public string? Occupation { get; private set; }
    public string? Company { get; private set; }
    public string? ShirtSize { get; private set; }
    public string? HearAbout { get; private set; }
    public int TicketVersion { get; private set; }

    public DateTimeOffset? CheckedInAt { get; private set; }
    public string? CheckedInStation { get; private set; }
    public Guid? CheckedInById { get; private set; }

    public bool IsBuyer => SortOrder == 0;
    public string FullName => MiddleName is null ? $"{FirstName} {LastName}" : $"{FirstName} {MiddleName} {LastName}";

    internal static Attendee Create(Guid registrationId, Guid eventId, int sortOrder, string participantId, AttendeeDetails d, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(participantId)) throw new DomainException("A participant ID is required.");
        if (string.IsNullOrWhiteSpace(d.FirstName) || string.IsNullOrWhiteSpace(d.LastName))
            throw new DomainException("First and last name are required.");
        if (!RegistrationForm.IsEmail(d.Email)) throw new DomainException("A valid email is required.");
        if (d.Mobile is not null && MobileNumber.Normalize(d.Mobile) != d.Mobile) throw new DomainException("Mobile must be +63 followed by 10 digits.");
        if (d.Gender is not null && !RegistrationForm.Genders.Contains(d.Gender)) throw new DomainException("Unknown gender option.");
        if (d.ShirtSize is not null && !RegistrationForm.ShirtSizes.Contains(d.ShirtSize)) throw new DomainException("Unknown shirt size.");
        if (d.HearAbout is not null && !RegistrationForm.HearAbout.Contains(d.HearAbout)) throw new DomainException("Unknown option.");
        return new Attendee
        {
            Id = Guid.CreateVersion7(at),
            RegistrationId = registrationId,
            EventId = eventId,
            SortOrder = sortOrder,
            ParticipantId = participantId,
            FirstName = d.FirstName.Trim(),
            MiddleName = Blank(d.MiddleName),
            LastName = d.LastName.Trim(),
            Gender = d.Gender,
            Email = d.Email.Trim(),
            Mobile = d.Mobile,
            Occupation = Blank(d.Occupation),
            Company = Blank(d.Company),
            ShirtSize = d.ShirtSize,
            HearAbout = d.HearAbout,
            TicketVersion = 1,
        };
    }

    internal void MarkCheckedIn(string station, Guid staffUserId, DateTimeOffset at)
    {
        if (CheckedInAt is not null) throw new DomainException("This attendee is already checked in.");
        CheckedInAt = at;
        CheckedInStation = station;
        CheckedInById = staffUserId;
    }

    /// <summary>Makes every QR issued so far invalid (the next ticket email carries the new version).</summary>
    internal void BumpTicketVersion() => TicketVersion++;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
