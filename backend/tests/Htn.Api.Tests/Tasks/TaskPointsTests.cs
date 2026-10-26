using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The points rule (frame 08 and the canvas note): base × role multiplier, awarded once when the task is approved
/// into Done, snapshotted, reversed with a reason when it is reopened.
/// </summary>
public sealed class TaskPointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>Award rows of a task, oldest round first and Lead → Collab → Assistant inside a round.</summary>
    private async Task<List<PointsAward>> AwardsAsync(Guid taskId) =>
        [.. (await factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => a.TaskId == taskId).ToListAsync()))
            .OrderBy(a => a.AwardedAt).ThenBy(a => a.Role)];

    [Fact]
    public async Task Approving_awards_each_role_once_with_the_multiplier_snapshotted()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Book venue", basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.OtherLead.PersonId, "Collab");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Assistant");

        // The projected split is on the detail before anything is awarded (the aside in frame 08).
        var before = await DetailAsync(s.Board, id);
        Assert.Equal([40.00m, 28.00m, 12.00m],
            before.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("points").GetDecimal()).ToList());
        Assert.Empty(before.GetProperty("awards").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.LeadClient, id, "Review")).StatusCode);
        var approved = await ApproveAsync(s.LeadClient, id);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var detail = await ApiClient.JsonAsync(approved);
        Assert.Equal("Done", detail.Status());
        Assert.NotNull(detail.GetProperty("approvedByName").GetString());
        Assert.Equal(3, detail.LiveAwards().Count);
        Assert.Equal([40.00m, 28.00m, 12.00m],
            detail.GetProperty("awards").EnumerateArray().Select(a => a.GetProperty("points").GetDecimal()).ToList());

        var awards = await AwardsAsync(id);
        Assert.Equal(3, awards.Count);
        Assert.All(awards, a => Assert.Equal(40, a.BasePoints));
        Assert.Equal([1.00m, 0.70m, 0.30m], awards.Select(a => a.Multiplier).ToList());
        Assert.All(awards, a => Assert.Null(a.ReversedAt));

        // Approving again changes nothing: the awards are already there.
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        Assert.Equal(3, (await AwardsAsync(id)).Count);

        // Both the task and every award row are audited.
        var awardIds = awards.Select(a => a.Id.ToString()).ToList();
        var actions = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == id.ToString() || awardIds.Contains(a.EntityId))
            .Select(a => a.Action).ToListAsync());
        Assert.Contains("task.approve", actions);
        Assert.Equal(3, actions.Count(a => a == "points.award"));
        Assert.Contains("approved", detail.ActivityKinds());
    }

    [Fact]
    public async Task Changing_the_multipliers_in_settings_leaves_old_awards_untouched()
    {
        var s = await SceneAsync(factory);
        var first = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Before", basePoints: 40);
        await AssignOkAsync(s.Board, first, s.Lead.PersonId, "Collab");
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(s.Board, first)).StatusCode); // no Lead yet
        await AssignOkAsync(s.Board, first, s.Worker.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, first)).StatusCode);
        var old = await AwardsAsync(first);
        Assert.Equal(28.00m, old.Single(a => a.Role == TaskAssigneeRole.Collab).Points);

        try
        {
            var changed = await s.Board.PutAsync("/api/settings/points", new
            {
                leadMultiplier = 2.0m, collabMultiplier = 1.0m, assistantMultiplier = 0.5m,
                presets = new { small = 10, medium = 20, large = 40, xl = 80 },
                requireTaskApproval = true,
            });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

            // History stands...
            var unchanged = await AwardsAsync(first);
            Assert.Equal(old.Select(a => (a.Multiplier, a.Points)), unchanged.Select(a => (a.Multiplier, a.Points)));

            // ...and only later awards use the new numbers.
            var second = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "After", basePoints: 40);
            await AssignOkAsync(s.Board, second, s.Lead.PersonId, "Lead");
            await AssignOkAsync(s.Board, second, s.Worker.PersonId, "Collab");
            Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, second)).StatusCode);
            var fresh = await AwardsAsync(second);
            Assert.Equal(80.00m, fresh.Single(a => a.Role == TaskAssigneeRole.Lead).Points);
            Assert.Equal(40.00m, fresh.Single(a => a.Role == TaskAssigneeRole.Collab).Points);
        }
        finally
        {
            await s.Board.PutAsync("/api/settings/points", new
            {
                leadMultiplier = 1.0m, collabMultiplier = 0.7m, assistantMultiplier = 0.3m,
                presets = new { small = 10, medium = 20, large = 40, xl = 80 },
                requireTaskApproval = true,
            });
        }
    }

    [Fact]
    public async Task An_assignee_reaches_Review_but_Done_needs_approval()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.WorkerClient, id, "Review")).StatusCode);
        var refused = await MoveAsync(s.WorkerClient, id, "Done");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("needs_approval", (await ApiClient.JsonAsync(refused)).Code());
        Assert.Empty(await AwardsAsync(id));
        Assert.Equal("Review", (await DetailAsync(s.Board, id)).Status());

        // The lead finishes it, from Review, through the same move endpoint.
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.LeadClient, id, "Done")).StatusCode);
        Assert.Equal(2, (await AwardsAsync(id)).Count);
    }

    [Fact]
    public async Task A_task_without_a_lead_cannot_be_approved()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var refused = await ApproveAsync(s.LeadClient, id);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("no_lead", (await ApiClient.JsonAsync(refused)).Code());
        // The same answer through the move endpoint, and nothing is awarded or moved.
        Assert.Equal("no_lead", (await ApiClient.JsonAsync(await MoveAsync(s.LeadClient, id, "Done"))).Code());
        Assert.Empty(await AwardsAsync(id));
        Assert.Equal("Backlog", (await DetailAsync(s.Board, id)).Status());
    }

    [Fact]
    public async Task Reopening_reverses_the_awards_and_always_records_why()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);

        // No reason, no reopen.
        var noReason = await ReopenAsync(s.LeadClient, id, "");
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.True((await ApiClient.JsonAsync(noReason)).HasError("reason"));
        Assert.Equal(2, (await AwardsAsync(id)).Count(a => a.ReversedAt is null));

        var reopened = await ReopenAsync(s.LeadClient, id, "Booths block exit B.");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var detail = await ApiClient.JsonAsync(reopened);
        Assert.Equal("Review", detail.Status());
        Assert.Empty(detail.LiveAwards());
        Assert.Equal(2, detail.GetProperty("awards").GetArrayLength()); // the reversed rows stay on the record
        Assert.Contains("reopened", detail.ActivityKinds());

        var awards = await AwardsAsync(id);
        Assert.All(awards, a => Assert.Equal("Booths block exit B.", a.ReversalReason));
        Assert.All(awards, a => Assert.NotNull(a.ReversedAt));
        var reversedIds = awards.Select(a => a.Id.ToString()).ToList();
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == "points.reverse" && reversedIds.Contains(a.EntityId))
            .Select(a => a.Reason).ToListAsync());
        Assert.Equal(2, audit.Count);
        Assert.All(audit, r => Assert.Equal("Booths block exit B.", r));

        // Reopening something that isn't done is refused.
        Assert.Equal("not_done", (await ApiClient.JsonAsync(await ReopenAsync(s.LeadClient, id, "Again."))).Code());

        // Finishing it again awards a fresh set; the reversed rows are untouched history.
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.LeadClient, id)).StatusCode);
        var after = await AwardsAsync(id);
        Assert.Equal(4, after.Count);
        Assert.Equal(2, after.Count(a => a.ReversedAt is null));
    }

    [Fact]
    public async Task A_done_task_freezes_its_points_assignees_and_checklist()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        var itemRes = await s.Board.PostAsync($"/api/tasks/{id}/checklist", new { text = "Sign the contract" });
        var itemId = (await ApiClient.JsonAsync(itemRes)).GetProperty("checklist").EnumerateArray().Single()
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);

        async Task AssertFrozenAsync(HttpResponseMessage res)
        {
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal("task_done", (await ApiClient.JsonAsync(res)).Code());
        }

        await AssertFrozenAsync(await s.Board.PutAsync($"/api/tasks/{id}", new { title = "Book venue", basePoints = 80 }));
        await AssertFrozenAsync(await AssignAsync(s.Board, id, s.Worker.PersonId, "Collab"));
        await AssertFrozenAsync(await s.Board.PostAsync($"/api/tasks/{id}/checklist", new { text = "One more" }));
        await AssertFrozenAsync(await s.Board.PutAsync($"/api/tasks/{id}/checklist/{itemId}", new { isDone = true }));
        await AssertFrozenAsync(await s.Board.PostFileAsync($"/api/tasks/{id}/attachments", Png, "a.png", "image/png"));
        // Moving out of Done goes through reopen, which reverses the awards with a reason.
        await AssertFrozenAsync(await MoveAsync(s.Board, id, "InProgress"));

        var detail = await DetailAsync(s.Board, id);
        Assert.Equal(40, detail.GetProperty("basePoints").GetInt32());
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.False(detail.GetProperty("canWork").GetBoolean());
        // Commenting stays open on a finished card.
        Assert.True(detail.GetProperty("canComment").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await s.Board.PostAsync($"/api/tasks/{id}/comments", new { body = "Filed." })).StatusCode);
    }

    [Fact]
    public async Task Volunteers_can_be_put_on_a_task_but_never_earn_points()
    {
        var s = await SceneAsync(factory);
        var volunteer = (await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/events/{s.EventId}/volunteers",
            new { name = "Carla Diaz", committeeId = s.Committee }))).GetProperty("id").GetGuid();
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");

        // A volunteer only ever helps as an Assistant.
        var asLead = await AssignAsync(s.Board, id, null, "Lead", volunteer);
        Assert.Equal(HttpStatusCode.BadRequest, asLead.StatusCode);
        Assert.Equal("volunteer_role", (await ApiClient.JsonAsync(asLead)).Code());

        var added = await AssignAsync(s.Board, id, null, "Assistant", volunteer);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var helper = (await ApiClient.JsonAsync(added)).GetProperty("assignees").EnumerateArray()
            .Single(a => a.GetProperty("isVolunteer").GetBoolean());
        Assert.Equal("Carla Diaz", helper.GetProperty("name").GetString());
        Assert.Equal(0m, helper.GetProperty("points").GetDecimal());
        Assert.Equal(0m, helper.GetProperty("multiplier").GetDecimal());
        Assert.Equal(JsonValueKind.Null, helper.GetProperty("personId").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        var awards = await AwardsAsync(id);
        Assert.Single(awards);
        Assert.Equal(s.Lead.PersonId, awards[0].PersonId);
        // Adding the same volunteer twice is a conflict, not a second helper.
        Assert.Equal(HttpStatusCode.Conflict, (await AssignAsync(s.Board, id, null, "Assistant", volunteer)).StatusCode);
    }

    [Fact]
    public async Task Two_approvals_at_the_same_time_award_exactly_one_set()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var results = await Task.WhenAll(ApproveAsync(s.Board, id), ApproveAsync(s.LeadClient, id));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var awards = await AwardsAsync(id);
        Assert.Equal(2, awards.Count);
        Assert.Equal(68.00m, awards.Sum(a => a.Points));
    }

    [Fact]
    public async Task Open_task_counts_feed_the_committee_cards_and_the_deactivate_guard()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");

        var page = await PageAsync(s.Board, s.EventId);
        Assert.Equal(1, page.Committee(s.Committee).GetProperty("openTasks").GetInt32());

        var refused = await s.Board.PostAsync($"/api/events/{s.EventId}/committees/{s.Committee}/deactivate",
            new { reason = "Cleaning up." });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("has_open_tasks", (await ApiClient.JsonAsync(refused)).Code());

        // Finishing the card clears the count, and the committee can be closed.
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        var cleared = await PageAsync(s.Board, s.EventId);
        Assert.Equal(0, cleared.Committee(s.Committee).GetProperty("openTasks").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/events/{s.EventId}/committees/{s.Committee}/deactivate",
                new { reason = "Cleaning up." })).StatusCode);
    }
}

/// <summary>With <c>RequireTaskApproval</c> off, anyone with <c>Tasks.Work</c> on the card can finish it.</summary>
public sealed class TaskApprovalOffTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task An_assignee_can_finish_a_card_when_approval_is_not_required()
    {
        var s = await SceneAsync(factory);
        var set = await s.Board.PutAsync("/api/settings/points", new
        {
            leadMultiplier = 1.0m, collabMultiplier = 0.7m, assistantMultiplier = 0.3m,
            presets = new { small = 10, medium = 20, large = 40, xl = 80 },
            requireTaskApproval = false,
        });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var moved = await MoveAsync(s.WorkerClient, id, "Done");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var detail = await ApiClient.JsonAsync(moved);
        Assert.Equal("Done", detail.Status());
        Assert.Equal(2, detail.LiveAwards().Count);
        Assert.False(detail.GetProperty("requireTaskApproval").GetBoolean());

        // Still nothing without a Lead, approval setting or not.
        var second = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, second, s.Worker.PersonId, "Assistant");
        Assert.Equal("no_lead", (await ApiClient.JsonAsync(await MoveAsync(s.WorkerClient, second, "Done"))).Code());
    }
}
