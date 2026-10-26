using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// Nobody signs off their own work (the user decision behind the Phase 4 review): the only <c>Lead</c> on a card can
/// never be the one who finishes it, whichever route they take — <c>POST /approve</c>, <c>POST /move</c> into Done,
/// and the same with approval switched off in Settings. Another lead of that committee, or any board member, always
/// can, so a card is never stuck; and a card with two Leads may be approved by one of them, because the other Lead is
/// the second pair of eyes.
/// </summary>
public sealed class TaskApprovalTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<List<PointsAward>> AwardsAsync(Guid taskId) =>
        factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => a.TaskId == taskId).ToListAsync());

    [Fact]
    public async Task The_only_lead_on_a_card_cannot_approve_it_themselves()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var refused = await ApproveAsync(s.LeadClient, id);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await ApiClient.JsonAsync(refused);
        Assert.Equal("needs_other_approver", problem.Code());
        Assert.Contains("board member", problem.GetProperty("title").GetString());
        Assert.Empty(await AwardsAsync(id));
        Assert.Equal("Backlog", (await DetailAsync(s.Board, id)).Status());

        // The same answer through the move endpoint, which is the drag onto Done.
        Assert.Equal("needs_other_approver", (await ApiClient.JsonAsync(await MoveAsync(s.LeadClient, id, "Done"))).Code());
        Assert.Empty(await AwardsAsync(id));

        // The dialog says why: canApprove is off and needsOtherApprover explains it, for her and nobody else.
        var hers = await DetailAsync(s.LeadClient, id);
        Assert.False(hers.GetProperty("canApprove").GetBoolean());
        Assert.True(hers.GetProperty("needsOtherApprover").GetBoolean());
        Assert.True(hers.GetProperty("canEdit").GetBoolean()); // everything else is still hers to do
        var board = await DetailAsync(s.Board, id);
        Assert.True(board.GetProperty("canApprove").GetBoolean());
        Assert.False(board.GetProperty("needsOtherApprover").GetBoolean());

        // A board member signs it off instead.
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        Assert.Equal(2, (await AwardsAsync(id)).Count);
    }

    [Fact]
    public async Task A_second_lead_on_the_card_is_the_second_pair_of_eyes()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        // Two Leads: the committee lead may approve, because the other Lead's work is being signed off too.
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Lead");

        var detail = await DetailAsync(s.LeadClient, id);
        Assert.True(detail.GetProperty("canApprove").GetBoolean());
        Assert.False(detail.GetProperty("needsOtherApprover").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.LeadClient, id)).StatusCode);
        var awards = await AwardsAsync(id);
        Assert.Equal(2, awards.Count);
        Assert.All(awards, a => Assert.Equal(TaskAssigneeRole.Lead, a.Role));
    }

    [Fact]
    public async Task A_new_lead_of_the_committee_can_approve_the_old_leads_card()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 30);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        Assert.Equal("needs_other_approver", (await ApiClient.JsonAsync(await ApproveAsync(s.LeadClient, id))).Code());

        // Hand the committee over to another goon: the new lead is nobody's Lead on this card, so she may approve it.
        var (successor, successorClient) = await Committees.CommitteeTestData.SignedInUserAsync(factory, Htn.Domain.Organization.PersonLevel.Goon);
        await Committees.CommitteeTestData.SetLeadAsync(s.Board, s.EventId, s.Committee, successor.PersonId);

        var detail = await DetailAsync(successorClient, id);
        Assert.True(detail.GetProperty("canApprove").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(successorClient, id)).StatusCode);
        Assert.Single(await AwardsAsync(id));
        // And the previous lead, now a plain member of the committee, is no longer a manager of the card.
        Assert.False((await DetailAsync(s.LeadClient, id)).GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task Adding_an_assignee_while_an_approval_lands_either_awards_them_or_fails()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        var approver = await factory.SignedInAsync(Htn.Domain.Organization.PersonLevel.Board);

        // The add and the approve race: both take the task row lock and re-read the row, so one of them loses.
        var add = AssignAsync(s.Board, id, s.Worker.PersonId, "Collab");
        var approve = ApproveAsync(approver, id);
        var added = await add;
        var approved = await approve;

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var awards = await AwardsAsync(id);
        var assignees = await factory.DbAsync(db => db.TaskAssignees.AsNoTracking()
            .Where(a => a.TaskId == id && a.PersonId != null).Select(a => a.PersonId!.Value).ToListAsync());

        if (added.StatusCode == HttpStatusCode.OK)
        {
            // The assignee went on before the approval: she is awarded, never silently dropped.
            Assert.Contains(s.Worker.PersonId, assignees);
            Assert.Equal(2, awards.Count);
        }
        else
        {
            // The approval went first, so the card was already frozen: 409, and she is not on it at all.
            Assert.Equal(HttpStatusCode.Conflict, added.StatusCode);
            Assert.Equal("task_done", (await ApiClient.JsonAsync(added)).Code());
            Assert.DoesNotContain(s.Worker.PersonId, assignees);
            Assert.Single(awards);
        }
        // Either way: exactly one live award per person on the card.
        Assert.Equal(assignees.Order(), awards.Where(a => a.ReversedAt is null).Select(a => a.PersonId).Order());
    }

    [Fact]
    public async Task A_done_card_still_says_who_may_reopen_archive_or_restore_it()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Lead");

        // While the card is open: archiving is on the table, reopening and restoring are not.
        var open = await DetailAsync(s.LeadClient, id);
        Assert.True(open.GetProperty("canArchive").GetBoolean());
        Assert.False(open.GetProperty("canReopen").GetBoolean());
        Assert.False(open.GetProperty("canRestore").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.LeadClient, id)).StatusCode);

        // Done: canEdit/canWork/canApprove are all false, so these are the only flags that can offer the undo.
        foreach (var manager in new[] { s.LeadClient, s.Board })
        {
            var detail = await DetailAsync(manager, id);
            Assert.False(detail.GetProperty("canEdit").GetBoolean());
            Assert.False(detail.GetProperty("canApprove").GetBoolean());
            Assert.True(detail.GetProperty("canReopen").GetBoolean());
            // The card holds live awards, so archiving would answer 409 reopen_first: not offered.
            Assert.False(detail.GetProperty("canArchive").GetBoolean());
            Assert.False(detail.GetProperty("canRestore").GetBoolean());
        }

        // The apprentice who did the work is not a manager: none of the three.
        var hers = await DetailAsync(s.WorkerClient, id);
        Assert.False(hers.GetProperty("canReopen").GetBoolean());
        Assert.False(hers.GetProperty("canArchive").GetBoolean());
        Assert.False(hers.GetProperty("canRestore").GetBoolean());
        // And the lead of the sibling committee, who can only read the card, likewise.
        var theirs = await DetailAsync(s.OtherLeadClient, id);
        Assert.False(theirs.GetProperty("canReopen").GetBoolean());
        Assert.False(theirs.GetProperty("canArchive").GetBoolean());

        // The flag is honest: the committee lead really can reopen it, which clears the awards...
        var reopened = await ApiClient.JsonAsync(await ReopenAsync(s.LeadClient, id, "Approved the wrong card."));
        Assert.Equal("Review", reopened.Status());
        Assert.False(reopened.GetProperty("canReopen").GetBoolean());
        // ...and archiving is offered again now that nothing is awarded.
        Assert.True(reopened.GetProperty("canArchive").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/tasks/{id}/archive", new { reason = "Duplicate card." })).StatusCode);
        var archived = await DetailAsync(s.LeadClient, id);
        Assert.True(archived.GetProperty("canRestore").GetBoolean());
        Assert.False(archived.GetProperty("canArchive").GetBoolean());
        Assert.False(archived.GetProperty("canReopen").GetBoolean());
        Assert.False((await DetailAsync(s.WorkerClient, id)).GetProperty("canRestore").GetBoolean());
        // And restoring really works for the manager the flag points at.
        var restored = await ApiClient.JsonAsync(await s.LeadClient.PostAsync($"/api/tasks/{id}/restore"));
        Assert.False(restored.GetProperty("canRestore").GetBoolean());
        Assert.True(restored.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task A_task_with_live_awards_cannot_be_archived_until_it_is_reopened()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);

        var refused = await s.Board.PostAsync($"/api/tasks/{id}/archive", new { reason = "Tidying the board." });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("reopen_first", (await ApiClient.JsonAsync(refused)).Code());
        Assert.False((await DetailAsync(s.Board, id)).GetProperty("isArchived").GetBoolean());
        Assert.Single(await AwardsAsync(id), a => a.ReversedAt is null);

        // Reopening reverses the awards with a reason; then the card may leave the board.
        Assert.Equal(HttpStatusCode.OK, (await ReopenAsync(s.Board, id, "Cancelled, not done.")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/tasks/{id}/archive", new { reason = "Tidying the board." })).StatusCode);
        Assert.DoesNotContain(await AwardsAsync(id), a => a.ReversedAt is null);
    }
}

/// <summary>
/// With <c>RequireTaskApproval</c> off anyone working the card may finish it — but a sole-Lead self-finish is still
/// refused, so switching approval off never becomes a way to award yourself points.
/// </summary>
public sealed class TaskSelfFinishWithoutApprovalTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task The_sole_lead_still_cannot_finish_their_own_card_with_approval_off()
    {
        var s = await SceneAsync(factory);
        var set = await s.Board.PutAsync("/api/settings/points", new
        {
            leadMultiplier = 1.0m, collabMultiplier = 0.7m, assistantMultiplier = 0.3m,
            presets = new { small = 10, medium = 20, large = 40, xl = 80 },
            requireTaskApproval = false,
        });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        try
        {
            var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
            // The apprentice is the only Lead on her own card: she may drag it to Review, not to Done.
            await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Lead");
            Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.WorkerClient, id, "Review")).StatusCode);
            var refused = await MoveAsync(s.WorkerClient, id, "Done");
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("needs_other_approver", (await ApiClient.JsonAsync(refused)).Code());
            Assert.Empty(await factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => a.TaskId == id).ToListAsync()));

            // The committee lead finishes it for her.
            Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.LeadClient, id, "Done")).StatusCode);
            Assert.Single(await factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => a.TaskId == id).ToListAsync()));
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
}
