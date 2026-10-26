using Htn.Domain.Organization;

namespace Htn.Api.Features.Committees;

/// <summary>
/// The Committees screen (frame 06) in one payload: every committee of the event with its lead, members, volunteers
/// and open-task count, plus the "Not yet assigned" pool. Permission flags are explicit so the UI never guesses from
/// the flat policy list in <c>/api/auth/me</c> (the committee policies are resource-scoped).
/// </summary>
public sealed record CommitteesPageDto(
    bool CanCreateCommittee, bool CanManageVolunteers, IReadOnlyList<CommitteeDto> Committees, CommitteePoolDto Pool);

public sealed record CommitteeDto(
    Guid Id, string Name, int SortOrder, bool IsActive, bool CanManage, int OpenTasks, int PeopleCount,
    CommitteePersonDto? Lead, IReadOnlyList<CommitteeMemberDto> Members, IReadOnlyList<VolunteerDto> Volunteers);

public sealed record CommitteePersonDto(Guid PersonId, string FirstName, string LastName, PersonLevel Level);

public sealed record CommitteeMemberDto(Guid PersonId, string FirstName, string LastName, PersonLevel Level, DateTimeOffset JoinedAt);

/// <summary>Contact details are only filled in for callers who may manage the volunteer (board or the committee lead).</summary>
public sealed record VolunteerDto(Guid Id, string Name, string? Email, string? Mobile);

/// <summary>On the event roster but in no committee: active goons and apprentices with a login, plus free volunteers.</summary>
public sealed record CommitteePoolDto(IReadOnlyList<CommitteePersonDto> People, IReadOnlyList<VolunteerDto> Volunteers);

/// <summary>Create and update body. A null sort order keeps/appends at the end.</summary>
public sealed record CommitteeRequest(string Name, int? SortOrder);

public sealed record CommitteeLeadRequest(Guid? PersonId);

public sealed record CommitteeMemberRequest(Guid? PersonId);

public sealed record VolunteerRequest(string Name, string? Email, string? Mobile, Guid? CommitteeId);

/// <summary>Nothing on a committee is deleted, so deactivating always records why.</summary>
public sealed record CommitteeReasonRequest(string Reason);

public static class CommitteeErrorCodes
{
    public const string DuplicateName = "duplicate_name";
    public const string HasOpenTasks = "has_open_tasks";
    public const string WrongLevel = "wrong_level";
    public const string AlreadyLead = "already_lead";
    public const string AlreadyInCommittee = "already_in_committee";
    public const string IsLead = "is_lead";
    public const string CommitteeInactive = "committee_inactive";
    public const string VolunteerInactive = "volunteer_inactive";
}
