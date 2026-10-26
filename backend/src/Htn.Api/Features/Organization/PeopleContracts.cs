using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

public static class AccountStatuses
{
    public const string Active = "Active";
    public const string InvitePending = "InvitePending";
    public const string NoLogin = "NoLogin";
    public const string Deactivated = "Deactivated";
}

/// <summary>
/// A roster row. For non-Board callers <see cref="Email"/> is masked ("m•••@hackthenorth.ph") and
/// <see cref="AccountStatus"/> / <see cref="LastSignInAt"/> are null.
/// </summary>
public sealed record PersonDto(
    Guid Id,
    string FirstName,
    string? MiddleName,
    string LastName,
    string Email,
    PersonLevel Level,
    string? Position,
    bool IsActive,
    string? AccountStatus,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    bool IsCfo,
    bool IsFinanceAssistant);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record CreatePersonRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string Email,
    PersonLevel? Level,
    string? Position);

/// <summary>Optional body for POST /api/people/{id}/deactivate.</summary>
public sealed record DeactivatePersonRequest(string? Reason);

/// <summary>Short reference to a person (position holders, finance roles).</summary>
public sealed record PersonRefDto(Guid Id, string FirstName, string LastName, PersonLevel Level);

public sealed record PersonPositionDto(Guid Id, string Title, PositionKind Kind);

public sealed record PersonHistoryDto(DateTimeOffset At, string Kind, string? From, string? To, string? Reason, string? ActorName);

/// <summary>An event this person checked in at (matched by email, case-insensitive), from check-in records.</summary>
public sealed record PersonAttendanceDto(Guid EventId, string EventName, DateTimeOffset CheckedInAt);

/// <summary>GET /api/people/{id}. <see cref="History"/> and <see cref="Attendance"/> are empty for non-Board callers.</summary>
public sealed record PersonDetailDto(
    PersonDto Person, IReadOnlyList<PersonPositionDto> Positions, IReadOnlyList<PersonHistoryDto> History,
    IReadOnlyList<PersonAttendanceDto> Attendance);

public sealed record PromotePersonRequest(PersonLevel? ToLevel, string? Reason);

public sealed record UpdatePersonRequest(string FirstName, string? MiddleName, string LastName, string Email, string? Position);

public sealed record ImportRowDto(int Row, string FirstName, string LastName, string Email, string Level, IReadOnlyList<string> Errors);

public sealed record ImportResultDto(IReadOnlyList<ImportRowDto> Rows, int Valid, int Invalid, int Created);
