using System.Text.RegularExpressions;
using Htn.Domain.Common;

namespace Htn.Domain.CheckIn;

/// <summary>What the desk shows after a scan or a manual check-in.</summary>
public enum CheckInResult
{
    /// <summary>First valid scan of a paid ticket: the attendee is now checked in.</summary>
    Welcome,

    /// <summary>The attendee was already checked in (the card shows when and at which station).</summary>
    AlreadyCheckedIn,

    /// <summary>The registration is still pending payment: send them to the help desk.</summary>
    NotPaid,

    /// <summary>The registration was cancelled.</summary>
    Cancelled,

    /// <summary>Bad signature, another event's ticket, an older ticket version, or an unknown participant ID.</summary>
    Invalid,
}

/// <summary>How the attendee was identified: a signed QR, or a desk lookup (participant ID typed or picked from search).</summary>
public enum CheckInMethod
{
    Qr,
    Manual,
}

public static class CheckInStation
{
    public const int MaxLength = 40;

    /// <summary>The trimmed station name, or null when it is empty or longer than <see cref="MaxLength"/>.</summary>
    public static string? Normalize(string? station)
    {
        var s = station?.Trim();
        return s is { Length: > 0 and <= MaxLength } && !s.Any(char.IsControl) ? s : null;
    }
}

/// <summary>Recognizes a typed participant ID (<c>HTN-XXXX-XXXX</c>, any case, dashes optional).</summary>
public static partial class ParticipantIdInput
{
    /// <summary>
    /// True when <paramref name="input"/> has the shape of a participant ID; <paramref name="participantId"/> is then the
    /// canonical upper-case, dashed form (which may still not exist, or use letters outside the alphabet).
    /// </summary>
    public static bool TryNormalize(string? input, out string participantId)
    {
        participantId = "";
        var s = input?.Trim();
        if (string.IsNullOrEmpty(s) || s.Length > 20) return false;
        var m = Shape().Match(s);
        if (!m.Success) return false;
        participantId = $"{PublicIdGenerator.ParticipantPrefix}-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}";
        return true;
    }

    [GeneratedRegex("^HTN-?([0-9A-Z]{4})-?([0-9A-Z]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}

/// <summary>
/// One scan or manual check-in at a desk, whatever the outcome (Invalid scans too, with no attendee). Append-only: the
/// database rejects UPDATE, DELETE and TRUNCATE. The raw QR payload is never stored.
/// </summary>
public sealed class CheckInScan
{
    private CheckInScan() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>The attendee of this event the scan resolved to; null for Invalid scans that matched no attendee here.</summary>
    public Guid? AttendeeId { get; private set; }

    public CheckInResult Result { get; private set; }
    public CheckInMethod Method { get; private set; }
    public string Station { get; private set; } = default!;
    public Guid StaffUserId { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static CheckInScan Create(
        Guid eventId, Guid? attendeeId, CheckInResult result, CheckInMethod method, string station, Guid staffUserId, DateTimeOffset at)
    {
        if (!Enum.IsDefined(result)) throw new DomainException("Unknown check-in result.");
        if (!Enum.IsDefined(method)) throw new DomainException("Unknown check-in method.");
        var s = CheckInStation.Normalize(station)
                ?? throw new DomainException($"Station name is required (at most {CheckInStation.MaxLength} characters).");
        if (staffUserId == Guid.Empty) throw new DomainException("The staff member is required.");
        if (attendeeId is null && result is not CheckInResult.Invalid)
            throw new DomainException("Only an invalid scan can have no attendee.");
        return new CheckInScan
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            AttendeeId = attendeeId,
            Result = result,
            Method = method,
            Station = s,
            StaffUserId = staffUserId,
            At = at,
        };
    }
}
