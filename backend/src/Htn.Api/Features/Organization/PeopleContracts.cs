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
    DateTimeOffset CreatedAt);

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
