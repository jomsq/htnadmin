using Htn.Domain.Organization;

namespace Htn.Api.Features.Leaderboard;

/// <summary>Which window the points are summed over (frame 14's segmented control).</summary>
public enum LeaderboardScope
{
    Event,
    Quarter,
    AllTime,
}

/// <summary>
/// Frame 14: the podium and the rankings table. <c>Rows</c> is already ranked and ordered; <c>Me</c> is the caller's
/// own row, repeated outside the list so the dashboard and the "your rank" line never have to search for it (null
/// when the caller can't be on the leaderboard at all, i.e. board members and people without a login).
/// </summary>
public sealed record LeaderboardDto(
    LeaderboardScope Scope,
    string ScopeLabel,
    Guid? EventId,
    Guid? CommitteeId,
    IReadOnlyList<LeaderboardFilterDto> Committees,
    IReadOnlyList<LeaderboardFilterDto> Events,
    IReadOnlyList<LeaderboardRowDto> Rows,
    LeaderboardRowDto? Me);

public sealed record LeaderboardFilterDto(Guid Id, string Name);

/// <summary>
/// One person's standing. <c>Rank</c> is <b>dense</b>: equal point totals share a rank and the next distinct total
/// takes the next integer (1, 1, 2), so the Rank column never skips. <c>TasksDone</c> counts the distinct tasks that
/// still hold a live award for this person; <c>AsLead</c>/<c>AsCollab</c>/<c>AsAssistant</c> count those live awards
/// by role. Reversed awards are excluded everywhere.
/// </summary>
public sealed record LeaderboardRowDto(
    int Rank,
    Guid PersonId,
    string Name,
    string Initials,
    PersonLevel Level,
    string Committees,
    int TasksDone,
    int AsLead,
    int AsCollab,
    int AsAssistant,
    decimal Points,
    bool IsMe);

public static class LeaderboardErrorCodes
{
    public const string EventRequired = "event_required";
}
