using Htn.Domain.CheckIn;
using Htn.Domain.Registrations;

namespace Htn.Api.Features.CheckIn;

/// <summary>POST /api/events/{id}/check-in/scan. <see cref="Payload"/> is the QR text or a typed participant ID.</summary>
public sealed record ScanRequest(string? Payload, string? Station);

/// <summary>POST /api/events/{id}/check-in/manual, after a desk search.</summary>
public sealed record ManualCheckInRequest(Guid? AttendeeId, string? Station);

/// <summary>The result card. Never includes email or mobile.</summary>
public sealed record CheckInAttendeeDto(string Name, string TicketName, string ParticipantId, string? ShirtSize, bool IsMember);

/// <summary>
/// <see cref="Attendee"/>, <see cref="AttendeeId"/> and <see cref="Status"/> are null only for Invalid.
/// <see cref="CheckedInAt"/> / <see cref="CheckedInStation"/>: the attendee's check-in when they have one (Welcome: just
/// now, here; AlreadyCheckedIn and Lookup: the first one). <see cref="AmountDue"/>: the registration total while it is
/// PendingPayment. <see cref="Status"/> is the attendee's own state (CheckedIn, or the registration's status, Paid for a
/// group partly in), so a Lookup card can show not paid / cancelled / already in. Lookup never changes anything: check
/// the person in with POST /manual { attendeeId, station }.
/// </summary>
public sealed record CheckInResultDto(
    CheckInResult Result, CheckInAttendeeDto? Attendee, DateTimeOffset? CheckedInAt, string? CheckedInStation, decimal? AmountDue,
    Guid? AttendeeId, RegistrationStatus? Status);

public sealed record CheckInStatsDto(int CheckedIn, int Paid, int ThisStation, int StationsOnline);

/// <summary>One recent scan. Name and participant ID are null for Invalid scans that matched no attendee.</summary>
public sealed record RecentScanDto(
    Guid Id, string? Name, string? ParticipantId, CheckInResult Result, CheckInMethod Method, string Station, DateTimeOffset At);

/// <summary>
/// Desk search hit (limited fields only). <see cref="Status"/> is this attendee's own state: CheckedIn when they have
/// checked in, otherwise the registration's status (Paid for a group whose other members are already in).
/// </summary>
public sealed record CheckInSearchHitDto(Guid AttendeeId, string Name, string ParticipantId, string TicketName, RegistrationStatus Status);
