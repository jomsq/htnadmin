using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Common;
using Htn.Domain.Tasks;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Leaderboard.LeaderboardTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Leaderboard;

/// <summary>
/// <c>GET /api/leaderboard</c> (frame 14): points from live awards, goons and apprentices only, dense ranking, the
/// caller's own row always present. Policy: <c>Leaderboard.View</c> — every signed-in staff member.
/// </summary>
public sealed class LeaderboardEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Sums_live_awards_per_person_with_the_role_split()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var venue = await TaskAsync(s, s.Logistics, 40);
        var badges = await TaskAsync(s, s.Logistics, 20);
        var stage = await TaskAsync(s, s.Programs, 40);
        await AwardAsync(factory, venue, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        await AwardAsync(factory, venue, s.Apprentice.PersonId, TaskAssigneeRole.Assistant, 0.3m);
        await AwardAsync(factory, badges, s.Goon.PersonId, TaskAssigneeRole.Collab, 0.7m);
        await AwardAsync(factory, stage, s.OtherGoon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.Equal("Event", page.GetProperty("scope").GetString());
        Assert.Equal(s.EventId, page.GetProperty("eventId").GetGuid());
        var goon = page.Row(s.Goon.PersonId);
        Assert.Equal(54.00m, goon.Points());                       // 40 × 1.0 + 20 × 0.7
        Assert.Equal(2, goon.GetProperty("tasksDone").GetInt32());
        Assert.Equal(1, goon.GetProperty("asLead").GetInt32());
        Assert.Equal(1, goon.GetProperty("asCollab").GetInt32());
        Assert.Equal(0, goon.GetProperty("asAssistant").GetInt32());
        Assert.Equal("Goon", goon.GetProperty("level").GetString());
        var apprentice = page.Row(s.Apprentice.PersonId);
        Assert.Equal(12.00m, apprentice.Points());
        Assert.Equal(1, apprentice.GetProperty("asAssistant").GetInt32());
        Assert.Equal("Apprentice", apprentice.GetProperty("level").GetString());
        // Highest first, with the committee the points were earned in and the initials for the podium.
        Assert.Equal([54.00m, 40.00m, 12.00m], page.Rows().Select(r => r.Points()).ToList());
        Assert.Equal([1, 2, 3], page.Rows().Select(r => r.Rank()).ToList());
        Assert.Contains("Logistics", goon.GetProperty("committees").GetString());
        Assert.Equal(2, goon.GetProperty("initials").GetString()!.Length);
    }

    [Fact]
    public async Task Board_members_and_members_are_never_ranked()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        // Board members can be assignees and do earn awards (frame 08); the leaderboard still leaves them out.
        await AwardAsync(factory, task, s.BoardUser.PersonId, TaskAssigneeRole.Lead, 1.0m);
        var other = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, other, s.Member.PersonId, TaskAssigneeRole.Assistant, 0.3m);
        var third = await TaskAsync(s, s.Logistics, 10);
        await AwardAsync(factory, third, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.False(page.Has(s.BoardUser.PersonId));
        Assert.False(page.Has(s.Member.PersonId));
        Assert.Single(page.Rows());
        Assert.Equal(10.00m, page.Row(s.Goon.PersonId).Points());
    }

    [Fact]
    public async Task Reversed_awards_are_excluded()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var kept = await TaskAsync(s, s.Logistics, 20);
        var reopened = await TaskAsync(s, s.Logistics, 80);
        await AwardAsync(factory, kept, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        await AwardAsync(factory, reopened, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m, reversedWith: "Venue fell through.");
        await AwardAsync(factory, reopened, s.Apprentice.PersonId, TaskAssigneeRole.Collab, 0.7m, reversedWith: "Venue fell through.");

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.Equal(20.00m, page.Row(s.Goon.PersonId).Points());
        Assert.Equal(1, page.Row(s.Goon.PersonId).GetProperty("tasksDone").GetInt32());
        // Nothing live is left for the apprentice, so she is off the table entirely.
        Assert.False(page.Has(s.Apprentice.PersonId));
    }

    /// <summary>
    /// Dense ranking (the choice recorded in the contract and in <c>LeaderboardRowDto</c>): equal totals share a rank
    /// and the next distinct total takes the next integer, so frame 14's Rank column reads 1, 1, 2 and never skips.
    /// </summary>
    [Fact]
    public async Task Ties_share_a_rank_and_the_next_rank_follows_densely()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var first = await TaskAsync(s, s.Logistics, 40);
        var second = await TaskAsync(s, s.Programs, 40);
        var third = await TaskAsync(s, s.Logistics, 10);
        await AwardAsync(factory, first, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        await AwardAsync(factory, second, s.OtherGoon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        await AwardAsync(factory, third, s.Apprentice.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.Equal([40.00m, 40.00m, 10.00m], page.Rows().Select(r => r.Points()).ToList());
        Assert.Equal([1, 1, 2], page.Rows().Select(r => r.Rank()).ToList());
        Assert.Equal(1, page.Row(s.Goon.PersonId).Rank());
        Assert.Equal(1, page.Row(s.OtherGoon.PersonId).Rank());
        Assert.Equal(2, page.Row(s.Apprentice.PersonId).Rank());
    }

    [Fact]
    public async Task Me_is_the_callers_row_and_is_marked_in_the_table()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, task, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.GoonClient, $"?scope=event&eventId={s.EventId}");

        var me = page.GetProperty("me");
        Assert.Equal(s.Goon.PersonId, me.GetProperty("personId").GetGuid());
        Assert.Equal(40.00m, me.Points());
        Assert.Equal(1, me.Rank());
        Assert.True(me.GetProperty("isMe").GetBoolean());
        Assert.True(page.Row(s.Goon.PersonId).GetProperty("isMe").GetBoolean());
    }

    [Fact]
    public async Task Me_is_present_with_zero_points_for_an_eligible_caller_without_awards()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, task, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.NewcomerClient, $"?scope=event&eventId={s.EventId}");

        var me = page.GetProperty("me");
        Assert.Equal(s.Newcomer.PersonId, me.GetProperty("personId").GetGuid());
        Assert.Equal(0m, me.Points());
        Assert.Equal(0, me.GetProperty("tasksDone").GetInt32());
        Assert.Equal(2, me.Rank()); // behind the single scoring row, dense
        Assert.True(me.GetProperty("isMe").GetBoolean());
        // Only people with live awards are listed, so the newcomer is not in the table itself.
        Assert.False(page.Has(s.Newcomer.PersonId));
    }

    [Fact]
    public async Task Me_is_null_for_a_board_caller()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, task, s.BoardUser.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.Equal(JsonValueKind.Null, page.GetProperty("me").ValueKind);
    }

    /// <summary>
    /// An award is a snapshot: its <c>committee_id</c> is where the points were earned, and the leaderboard reads
    /// that, never the task's current committee. A task can be moved between committees afterwards
    /// (<c>TaskItem.MoveToCommittee</c>, through <c>PUT /api/tasks/{id}</c>) and the past must not follow it.
    /// <para>
    /// The award is written through the domain factory, exactly as the approve path does, because the API refuses to
    /// edit a task that is already Done — which is also why this invariant is a backstop rather than a daily event.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_award_stays_with_the_committee_it_was_earned_in_when_the_task_moves()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, task, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        var names = (await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}")).GetProperty("committees")
            .EnumerateArray().ToDictionary(c => c.GetProperty("id").GetGuid(), c => c.GetProperty("name").GetString());

        var moved = await s.Board.PutAsync($"/api/tasks/{task}", new { title = "Book venue", committeeId = s.Programs });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(s.Programs, (await DetailAsync(s.Board, task)).GetProperty("committeeId").GetGuid());

        // The award is still Logistics': the filter finds it there and not under Programs.
        var logistics = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}&committeeId={s.Logistics}");
        var programs = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}&committeeId={s.Programs}");
        Assert.Equal(40.00m, logistics.Row(s.Goon.PersonId).Points());
        Assert.False(programs.Has(s.Goon.PersonId));
        // And the Committee column of frame 14 still reads Logistics.
        var all = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");
        Assert.Equal(names[s.Logistics], all.Row(s.Goon.PersonId).GetProperty("committees").GetString());
    }

    [Fact]
    public async Task Committee_filter_narrows_the_table()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var logistics = await TaskAsync(s, s.Logistics, 40);
        var programs = await TaskAsync(s, s.Programs, 40);
        await AwardAsync(factory, logistics, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);
        await AwardAsync(factory, programs, s.OtherGoon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}&committeeId={s.Logistics}");

        Assert.Equal(s.Logistics, page.GetProperty("committeeId").GetGuid());
        Assert.Single(page.Rows());
        Assert.True(page.Has(s.Goon.PersonId));
        Assert.False(page.Has(s.OtherGoon.PersonId));
    }

    [Fact]
    public async Task Event_filter_keeps_other_events_out()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var here = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, here, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        // A second event with its own committee and its own award for the same goon.
        var otherEvent = await OnSaleAsync(s.Board);
        var otherCommittee = await AddCommitteeAsync(s.Board, otherEvent, "Marketing " + Guid.NewGuid().ToString("N")[..6]);
        var there = await CreateTaskAsync(s.Board, otherEvent, otherCommittee, basePoints: 10);
        await AwardAsync(factory, there, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m);

        Assert.Equal(40.00m, (await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}")).Row(s.Goon.PersonId).Points());
        Assert.Equal(10.00m, (await GetAsync(s.Board, $"?scope=event&eventId={otherEvent}")).Row(s.Goon.PersonId).Points());
        // All time, unfiltered, adds both up and lists the committees the points came from.
        var all = await GetAsync(s.Board, "?scope=alltime");
        var row = all.Row(s.Goon.PersonId);
        Assert.Equal(50.00m, row.Points());
        Assert.Equal(2, row.GetProperty("tasksDone").GetInt32());
        Assert.Contains(",", row.GetProperty("committees").GetString());
    }

    /// <summary>The quarter is the calendar quarter in Asia/Manila: an award a second before the edge is last quarter's.</summary>
    [Fact]
    public async Task Quarter_scope_cuts_at_the_manila_quarter_boundary()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var window = ManilaTime.Quarter(DateTimeOffset.UtcNow);
        var before = await TaskAsync(s, s.Logistics, 40);
        var atStart = await TaskAsync(s, s.Logistics, 20);
        var after = await TaskAsync(s, s.Logistics, 10);
        await AwardAsync(factory, before, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m, window.Start.AddSeconds(-1));
        await AwardAsync(factory, atStart, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m, window.Start);
        await AwardAsync(factory, after, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m, window.Start.AddSeconds(1));
        // The next quarter has not happened yet: an award stamped at the far edge must not count either.
        var next = await TaskAsync(s, s.Logistics, 80);
        await AwardAsync(factory, next, s.Goon.PersonId, TaskAssigneeRole.Lead, 1.0m, window.End);

        var quarter = await GetAsync(s.Board, $"?scope=quarter&eventId={s.EventId}");
        var allTime = await GetAsync(s.Board, $"?scope=alltime&eventId={s.EventId}");

        Assert.Equal("This quarter", quarter.GetProperty("scopeLabel").GetString());
        Assert.Equal(30.00m, quarter.Row(s.Goon.PersonId).Points());   // 20 at the edge + 10 inside
        Assert.Equal(2, quarter.Row(s.Goon.PersonId).GetProperty("tasksDone").GetInt32());
        Assert.Equal(150.00m, allTime.Row(s.Goon.PersonId).Points());  // every live award, whenever it happened
        Assert.Equal("All time", allTime.GetProperty("scopeLabel").GetString());
    }

    [Fact]
    public async Task Quarter_is_the_default_scope_and_lists_the_filters()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);

        var page = await GetAsync(s.Board, "");

        Assert.Equal("Quarter", page.GetProperty("scope").GetString());
        Assert.Equal("This quarter", page.GetProperty("scopeLabel").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("eventId").ValueKind);
        Assert.Equal(JsonValueKind.Null, page.GetProperty("committeeId").ValueKind);
        var committees = page.GetProperty("committees").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(s.Logistics, committees);
        Assert.Contains(s.Programs, committees);
        Assert.Contains(s.EventId, page.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));

        // With an event chosen, the committee filter is that event's committees only.
        var scoped = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");
        Assert.Equal([s.Logistics, s.Programs],
            scoped.GetProperty("committees").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList());
        var ev = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}"));
        Assert.Equal(ev.GetProperty("name").GetString(), scoped.GetProperty("scopeLabel").GetString());
    }

    [Fact]
    public async Task Event_scope_without_an_event_is_a_field_error()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);

        var res = await s.Board.GetAsync("/api/leaderboard?scope=event");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.True(body.GetProperty("errors").TryGetProperty("eventId", out _));
        Assert.Equal("event_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_scope_is_a_field_error_and_a_missing_event_is_404()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Board.GetAsync("/api/leaderboard?scope=forever")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Board.GetAsync($"/api/leaderboard?scope=event&eventId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Goons_and_apprentices_may_read_it_and_anonymous_callers_may_not()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var anonymous = factory.NewClient();

        Assert.Equal(HttpStatusCode.OK, (await s.GoonClient.GetAsync("/api/leaderboard?scope=quarter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.ApprenticeClient.GetAsync("/api/leaderboard?scope=quarter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync("/api/leaderboard?scope=quarter")).StatusCode);
        // No cookie at all: the cookie handler answers 401 (the SPA then redirects to the sign-in screen).
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/leaderboard?scope=quarter")).StatusCode);
    }

    /// <summary>
    /// "My tasks" moved from <c>Events.View</c> to <c>Leaderboard.View</c> in this slice: same sidebar-wide audience,
    /// a policy that says what it gates. The route must keep working for every staff role, and still refuse anonymous.
    /// </summary>
    [Fact]
    public async Task My_tasks_still_works_under_the_new_policy()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AssignOkAsync(s.Board, task, s.Apprentice.PersonId, "Assistant");

        var mine = await s.ApprenticeClient.GetAsync("/api/tasks/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var body = await ApiClient.JsonAsync(mine);
        Assert.Equal(1, body.GetProperty("openCount").GetInt32());
        Assert.Equal(task, body.GetProperty("groups").EnumerateArray().Single()
            .GetProperty("tasks").EnumerateArray().Single().GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await s.GoonClient.GetAsync("/api/tasks/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Board.GetAsync("/api/tasks/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/tasks/mine")).StatusCode);
    }

    /// <summary>Levels are current, not historical: promoting an apprentice shows her as a goon on the same points.</summary>
    [Fact]
    public async Task Level_shown_is_the_persons_current_level()
    {
        var s = await LeaderboardTestData.SceneAsync(factory);
        var task = await TaskAsync(s, s.Logistics, 40);
        await AwardAsync(factory, task, s.Apprentice.PersonId, TaskAssigneeRole.Lead, 1.0m);
        var promoted = await s.Board.PostAsync($"/api/people/{s.Apprentice.PersonId}/promote",
            new { toLevel = "Goon", reason = "Ran the venue switch." });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        var page = await GetAsync(s.Board, $"?scope=event&eventId={s.EventId}");

        Assert.Equal("Goon", page.Row(s.Apprentice.PersonId).GetProperty("level").GetString());
        Assert.Equal(40.00m, page.Row(s.Apprentice.PersonId).Points());
    }
}
