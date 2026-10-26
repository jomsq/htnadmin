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
/// <see cref="CheckedInAt"/> / <see cref="CheckedInStation"/> are set for Welcome (just now, here) and AlreadyCheckedIn
/// (the first check-in). <see cref="AmountDue"/> is set for NotPaid. <see cref="Attendee"/> is null for Invalid.
/// </summary>
public sealed record CheckInResultDto(
    CheckInResult Result, CheckInAttendeeDto? Attendee, DateTimeOffset? CheckedInAt, string? CheckedInStation, decimal? AmountDue);

public sealed record CheckInStatsDto(int CheckedIn, int Paid, int ThisStation, int StationsOnline);

/// <summary>One recent scan. Name and participant ID are null for Invalid scans that matched no attendee.</summary>
public sealed record RecentScanDto(
    Guid Id, string? Name, string? ParticipantId, CheckInResult Result, CheckInMethod Method, string Station, DateTimeOffset At);

/// <summary>
/// Desk search hit (limited fields only). <see cref="Status"/> is this attendee's own state: CheckedIn when they have
/// checked in, otherwise the registration's status (Paid for a group whose other members are already in).
/// </summary>
public sealed record CheckInSearchHitDto(Guid AttendeeId, string Name, string ParticipantId, string TicketName, RegistrationStatus Status);
