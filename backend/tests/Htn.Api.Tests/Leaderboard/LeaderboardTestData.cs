using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Leaderboard;

/// <summary>
/// The cast for frame 14: one event with two committees, a goon lead per committee, an apprentice on the first
/// committee, an apprentice with nothing to show yet, plus the two people who must never be ranked — the board member
/// who signed in and a member with no login.
/// <para>
/// Every test builds its own event and asks for <c>eventId=</c>, so the class fixture's shared database can't let one
/// test's awards leak into another's table.
/// </para>
/// </summary>
internal sealed record LeaderboardScene(
    ApiClient Board,
    HtnApiFactory.TestUser BoardUser,
    Guid EventId,
    Guid Logistics,
    Guid Programs,
    HtnApiFactory.TestUser Goon,
    ApiClient GoonClient,
    HtnApiFactory.TestUser OtherGoon,
    HtnApiFactory.TestUser Apprentice,
    ApiClient ApprenticeClient,
    HtnApiFactory.TestUser Newcomer,
    ApiClient NewcomerClient,
    HtnApiFactory.TestUser Member);

internal static class LeaderboardTestData
{
    public static async Task<LeaderboardScene> SceneAsync(HtnApiFactory factory)
    {
        var (boardUser, board) = await SignedInUserAsync(factory, PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var logistics = await AddCommitteeAsync(board, eventId, "Logistics " + Guid.NewGuid().ToString("N")[..6]);
        var programs = await AddCommitteeAsync(board, eventId, "Programs " + Guid.NewGuid().ToString("N")[..6]);

        var (goon, goonClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (otherGoon, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, logistics, goon.PersonId);
        await SetLeadAsync(board, eventId, programs, otherGoon.PersonId);

        var (apprentice, apprenticeClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var added = await board.PostAsync($"/api/events/{eventId}/committees/{logistics}/members", new { personId = apprentice.PersonId });
        if (added.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Add member failed: {(int)added.StatusCode} {await added.Content.ReadAsStringAsync()}");

        var (newcomer, newcomerClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var member = await factory.CreateUserAsync(PersonLevel.Member);

        return new LeaderboardScene(board, boardUser, eventId, logistics, programs, goon, goonClient, otherGoon,
            apprentice, apprenticeClient, newcomer, newcomerClient, member);
    }

    /// <summary>
    /// Writes a live award straight to the table. The API only ever awards at "now", so tests that need a specific
    /// instant (the quarter boundaries) or an ineligible recipient (board, member) go through the domain factory and
    /// the same check constraints as the approve path.
    /// </summary>
    public static Task AwardAsync(
        HtnApiFactory factory, Guid taskId, Guid personId, TaskAssigneeRole role, decimal multiplier,
        DateTimeOffset? at = null, string? reversedWith = null) =>
        factory.DbAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            var when = at ?? DateTimeOffset.UtcNow;
            var award = PointsAward.Create(task, personId, role, multiplier, when, null);
            if (reversedWith is not null) award.Reverse(reversedWith, when.AddMinutes(1), null);
            db.PointsAwards.Add(award);
            await db.SaveChangesAsync();
            return award.Points;
        });

    /// <summary>A task to hang awards on. Points come from the award row, so only the base points matter here.</summary>
    public static Task<Guid> TaskAsync(LeaderboardScene scene, Guid committeeId, int basePoints) =>
        CreateTaskAsync(scene.Board, scene.EventId, committeeId, basePoints: basePoints);

    public static async Task<JsonElement> GetAsync(ApiClient client, string query)
    {
        var res = await client.GetAsync("/api/leaderboard" + query);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Leaderboard failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static List<JsonElement> Rows(this JsonElement page) => [.. page.GetProperty("rows").EnumerateArray()];

    public static List<string> Names(this JsonElement page) =>
        [.. page.Rows().Select(r => r.GetProperty("name").GetString()!)];

    public static JsonElement Row(this JsonElement page, Guid personId) =>
        page.Rows().Single(r => r.GetProperty("personId").GetGuid() == personId);

    public static bool Has(this JsonElement page, Guid personId) =>
        page.Rows().Any(r => r.GetProperty("personId").GetGuid() == personId);

    public static decimal Points(this JsonElement row) => row.GetProperty("points").GetDecimal();

    public static int Rank(this JsonElement row) => row.GetProperty("rank").GetInt32();
}
