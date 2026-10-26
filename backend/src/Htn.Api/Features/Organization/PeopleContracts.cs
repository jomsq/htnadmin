using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

public static class AccountStatuses
{
    public const string Active = "Active";
    public const string InvitePending = "InvitePending";
    public const string NoLogin = "NoLogin";
    public const string Deactivated = "Deactivated";
}

public sealed record PersonDto(
    Guid Id,
    string FirstName,
    string? MiddleName,
    string LastName,
    string Email,
    PersonLevel Level,
    string? Position,
    bool IsActive,
    string AccountStatus,
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
