using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Committees;
using Htn.Api.Features.Events;
using Htn.Api.Features.Tasks;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Leaderboard;

/// <summary>
/// Frame 14: points from completed tasks, goons and apprentices only. Read-only, and open to every signed-in
/// Board/Goon/Apprentice (<c>Leaderboard.View</c>) — unlike <c>Tasks.ViewBoard</c> it is not event-scoped, because the
/// screen lives in the sidebar and shows the whole organization.
/// <para>
/// Board members earn points on tasks (frame 08 shows it) but are never listed here, per the rules. Reversed awards
/// are excluded. The whole table comes from one grouped aggregate over <c>points_awards</c> (plus the two small filter
/// lists and, when needed, the caller's own person row) — never a query per person.
/// </para>
/// </summary>
public static class LeaderboardEndpoints
{
    /// <summary>Only these two levels are ranked (board members are excluded even though they can earn awards).</summary>
    private static readonly PersonLevel[] RankedLevels = [PersonLevel.Goon, PersonLevel.Apprentice];

    public static RouteGroupBuilder MapLeaderboard(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/leaderboard").WithTags("Leaderboard");
        g.MapGet("/", Get).RequireAuthorization(Policies.LeaderboardView);
        return g;
    }

    private static async Task<IResult> Get(
        string? scope, Guid? eventId, Guid? committeeId, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!TryParseScope(scope, out var window)) return Problems.Field("scope", "Unknown scope.");
        if (window == LeaderboardScope.Event && eventId is null)
            return Problems.Field("eventId", "Pick an event for the event leaderboard.", LeaderboardErrorCodes.EventRequired);

        var manager = await EventViews.CanManageAsync(http, auth);
        Event? chosen = null;
        if (eventId is { } id)
        {
            chosen = await CommitteeViews.VisibleEventAsync(http, auth, db, id, ct);
            if (chosen is null) return TypedResults.NotFound();
        }

        var quarter = ManilaTime.Quarter(clock.GetUtcNow());
        var rows = await RankedRowsAsync(db, window, chosen?.Id, committeeId, quarter, ct);

        // ---- the caller's own row: always present when they are eligible, even with no awards at all ----
        var mine = await MyPersonAsync(db, me.PersonId, ct);
        LeaderboardRowDto? meRow = null;
        if (mine is not null)
        {
            var existing = rows.FindIndex(r => r.PersonId == mine.Id);
            if (existing >= 0)
            {
                meRow = rows[existing] with { IsMe = true };
                rows[existing] = meRow;
            }
            else
            {
                // A zero-points row ranks after every scoring row, so adding it never shifts anybody else's rank.
                meRow = new LeaderboardRowDto(NextDenseRank(rows), mine.Id, mine.Name, TaskViews.Initials(mine.Name),
                    mine.Level, "", 0, 0, 0, 0, 0m, IsMe: true);
            }
        }

        return TypedResults.Ok(new LeaderboardDto(
            window, ScopeLabel(window, chosen, quarter), chosen?.Id, committeeId,
            await CommitteeFilterAsync(db, chosen?.Id, ct), await EventFilterAsync(db, manager, ct), rows, meRow));
    }

    // ---------- Scope ----------

    private static bool TryParseScope(string? scope, out LeaderboardScope window)
    {
        // The frame opens on "This quarter", so an omitted scope means quarter.
        if (string.IsNullOrWhiteSpace(scope))
        {
            window = LeaderboardScope.Quarter;
            return true;
        }
        return Enum.TryParse(scope, ignoreCase: true, out window) && Enum.IsDefined(window);
    }

    private static string ScopeLabel(LeaderboardScope window, Event? chosen, QuarterWindow quarter) => window switch
    {
        LeaderboardScope.Event => chosen?.Name ?? "This event",
        LeaderboardScope.Quarter => "This quarter",
        _ => "All time",
    };

    // ---------- The table ----------

    /// <summary>
    /// One grouped aggregate per (person, committee): the points sum, the distinct tasks and the live awards by role.
    /// Folded to one row per person in memory, which also builds the committee column — a task belongs to exactly one
    /// committee, so the per-committee distinct task counts add up to the person's distinct tasks.
    /// <para>
    /// <b>Attribution is the award's own snapshot.</b> Both the committee filter and the committee label come from
    /// <c>points_awards.committee_id</c>, which is authoritative: a task can later be moved to another committee
    /// (<c>TaskItem.MoveToCommittee</c>) and that must never retro-change points already earned. So this query joins
    /// <c>committees</c> only to resolve that snapshotted id to a name, and never joins <c>tasks</c>.
    /// </para>
    /// </summary>
    private static async Task<List<LeaderboardRowDto>> RankedRowsAsync(
        HtnDbContext db, LeaderboardScope window, Guid? eventId, Guid? committeeId, QuarterWindow quarter,
        CancellationToken ct)
    {
        var awards = db.PointsAwards.AsNoTracking().Where(a => a.ReversedAt == null);
        if (eventId is { } ev) awards = awards.Where(a => a.EventId == ev);
        if (committeeId is { } cid) awards = awards.Where(a => a.CommitteeId == cid);
        if (window == LeaderboardScope.Quarter)
            awards = awards.Where(a => a.AwardedAt >= quarter.Start && a.AwardedAt < quarter.End);

        var buckets = await (
            from a in awards
            join p in db.People.AsNoTracking() on a.PersonId equals p.Id
            // The award's snapshotted committee, resolved to its name. Never t.CommitteeId: see the summary.
            join c in db.Committees.AsNoTracking() on a.CommitteeId equals c.Id
            where p.IsActive && RankedLevels.Contains(p.Level)
            group new { a.TaskId, a.Role, a.Points } by new
            {
                a.PersonId,
                p.FirstName,
                p.LastName,
                p.Level,
                Committee = c.Name,
            }
            into g
            select new
            {
                g.Key.PersonId,
                g.Key.FirstName,
                g.Key.LastName,
                g.Key.Level,
                g.Key.Committee,
                Points = g.Sum(x => x.Points),
                TasksDone = g.Select(x => x.TaskId).Distinct().Count(),
                AsLead = g.Count(x => x.Role == TaskAssigneeRole.Lead),
                AsCollab = g.Count(x => x.Role == TaskAssigneeRole.Collab),
                AsAssistant = g.Count(x => x.Role == TaskAssigneeRole.Assistant),
            }).ToListAsync(ct);

        var people = buckets.GroupBy(b => b.PersonId).Select(g =>
        {
            var first = g.First();
            var name = $"{first.FirstName} {first.LastName}".Trim();
            return new LeaderboardRowDto(
                Rank: 0, g.Key, name, TaskViews.Initials(name), first.Level,
                // The committees the points were earned in, strongest first (frame 14's Committee column).
                Committees: string.Join(", ", g.OrderByDescending(b => b.Points).ThenBy(b => b.Committee, StringComparer.Ordinal)
                    .Select(b => b.Committee).Distinct(StringComparer.Ordinal)),
                TasksDone: g.Sum(b => b.TasksDone),
                AsLead: g.Sum(b => b.AsLead),
                AsCollab: g.Sum(b => b.AsCollab),
                AsAssistant: g.Sum(b => b.AsAssistant),
                Points: g.Sum(b => b.Points),
                IsMe: false);
        })
            .OrderByDescending(r => r.Points).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.PersonId)
            .ToList();

        return Rank(people);
    }

    /// <summary>
    /// Dense ranking: equal point totals share a rank and the next distinct total takes the next integer (1, 1, 2),
    /// so frame 14's Rank column never skips a number. Input must already be ordered by points, descending.
    /// </summary>
    private static List<LeaderboardRowDto> Rank(List<LeaderboardRowDto> ordered)
    {
        var rank = 0;
        decimal? previous = null;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (previous != ordered[i].Points)
            {
                rank++;
                previous = ordered[i].Points;
            }
            ordered[i] = ordered[i] with { Rank = rank };
        }
        return ordered;
    }

    /// <summary>Where a person with no points lands under dense ranking: sharing the last rank if that is 0 too.</summary>
    private static int NextDenseRank(List<LeaderboardRowDto> rows) =>
        rows.Count == 0 ? 1 : rows[^1].Points == 0m ? rows[^1].Rank : rows[^1].Rank + 1;

    private sealed record MePerson(Guid Id, string Name, PersonLevel Level);

    /// <summary>The caller as a leaderboard entrant, or null when they can never be on it (board, or no login).</summary>
    private static async Task<MePerson?> MyPersonAsync(HtnDbContext db, Guid? personId, CancellationToken ct)
    {
        if (personId is not { } id) return null;
        return await db.People.AsNoTracking()
            .Where(p => p.Id == id && p.IsActive && RankedLevels.Contains(p.Level))
            .Select(p => new MePerson(p.Id, p.FirstName + " " + p.LastName, p.Level))
            .SingleOrDefaultAsync(ct);
    }

    // ---------- Filters ----------

    private static async Task<IReadOnlyList<LeaderboardFilterDto>> CommitteeFilterAsync(
        HtnDbContext db, Guid? eventId, CancellationToken ct)
    {
        var query = db.Committees.AsNoTracking().Where(c => c.IsActive);
        if (eventId is { } id) query = query.Where(c => c.EventId == id);
        return await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new LeaderboardFilterDto(c.Id, c.Name)).ToListAsync(ct);
    }

    /// <summary>The scope picker's event list. Drafts stay hidden without Events.Manage, as everywhere else.</summary>
    private static async Task<IReadOnlyList<LeaderboardFilterDto>> EventFilterAsync(
        HtnDbContext db, bool manager, CancellationToken ct)
    {
        var query = db.Events.AsNoTracking();
        if (!manager) query = query.Where(e => e.Status != EventStatus.Draft);
        return await query.OrderByDescending(e => e.StartsAt).ThenBy(e => e.Name)
            .Select(e => new LeaderboardFilterDto(e.Id, e.Name)).ToListAsync(ct);
    }
}
