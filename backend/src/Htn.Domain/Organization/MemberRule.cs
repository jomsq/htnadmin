namespace Htn.Domain.Organization;

/// <summary>
/// To become a Member a person must have checked in at at least one event, verified from check-in records (an attendee
/// with the same email, case-insensitive, and a check-in time). Applies when a person is created at Member or their level
/// changes to Member. The roster import is exempt (existing members are brought in as they are) and says so in history.
/// </summary>
public static class MemberRule
{
    public const string ErrorCode = "no_check_in";
    public const string ImportedReason = "imported";
    public const string SeededReason = "seeded";

    public const string Message =
        "To become a Member, this person must have checked in at an event with the same email address.";

    public static bool RequiresCheckIn(PersonLevel level) => level == PersonLevel.Member;
}
