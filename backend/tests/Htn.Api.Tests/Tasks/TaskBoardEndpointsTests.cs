using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>The board itself (frame 07): reading it, creating cards, the filters, "My tasks" and who is refused.</summary>
public sealed class TaskBoardEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Board_shows_five_columns_with_cards_the_design_asks_for()
    {
        var s = await SceneAsync(factory);
        var due = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Finalize venue floor plan", basePoints: 40,
            status: "InProgress", dueAt: due, labels: ["Venue"]);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Assistant");
        await s.Board.PostAsync($"/api/tasks/{id}/checklist", new { text = "Get SMX CAD file" });
        var second = await s.Board.PostAsync($"/api/tasks/{id}/checklist", new { text = "Place sponsor booths" });
        var itemId = (await ApiClient.JsonAsync(second)).GetProperty("checklist").EnumerateArray()
            .Last().GetProperty("id").GetGuid();
        await s.Board.PutAsync($"/api/tasks/{id}/checklist/{itemId}", new { isDone = true });

        var board = await BoardAsync(s.Board, s.EventId);
        Assert.True(board.GetProperty("canCreateTask").GetBoolean());
        Assert.Equal(["Backlog", "ToDo", "InProgress", "Review", "Done"],
            board.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("status").GetString()).ToList());
        Assert.Equal(1, board.Column("InProgress").GetProperty("count").GetInt32());

        var card = board.Card(id);
        Assert.StartsWith("HTN-", card.GetProperty("reference").GetString());
        Assert.Equal(40, card.GetProperty("basePoints").GetInt32());
        Assert.Equal(1, card.GetProperty("checklistDone").GetInt32());
        Assert.Equal(2, card.GetProperty("checklistTotal").GetInt32());
        Assert.True(card.GetProperty("dueSoon").GetBoolean());
        Assert.False(card.GetProperty("overdue").GetBoolean());
        Assert.Equal(["Venue"], card.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToList());
        Assert.True(card.GetProperty("canMove").GetBoolean());
        Assert.Equal(["Lead", "Assistant"],
            card.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("role").GetString()).ToList());
        Assert.All(card.GetProperty("assignees").EnumerateArray(),
            a => Assert.Equal(2, a.GetProperty("initials").GetString()!.Length));

        // The lead sees their own committee as manageable, the sibling committee not.
        var leadView = await BoardAsync(s.LeadClient, s.EventId);
        Assert.True(leadView.GetProperty("canCreateTask").GetBoolean());
        var chips = leadView.GetProperty("committees").EnumerateArray().ToList();
        Assert.True(chips.Single(c => c.GetProperty("id").GetGuid() == s.Committee).GetProperty("canManage").GetBoolean());
        Assert.False(chips.Single(c => c.GetProperty("id").GetGuid() == s.OtherCommittee).GetProperty("canManage").GetBoolean());

        // An apprentice on the event sees the board but may not create cards, and may only move her own.
        var workerView = await BoardAsync(s.WorkerClient, s.EventId);
        Assert.False(workerView.GetProperty("canCreateTask").GetBoolean());
        Assert.True(workerView.Card(id).GetProperty("canMove").GetBoolean());
        Assert.True(workerView.Card(id).GetProperty("isMine").GetBoolean());

        var otherLeadView = await BoardAsync(s.OtherLeadClient, s.EventId);
        Assert.False(otherLeadView.Card(id).GetProperty("canMove").GetBoolean());
        Assert.False(otherLeadView.Card(id).GetProperty("isMine").GetBoolean());
    }

    [Fact]
    public async Task Overdue_cards_are_flagged_and_due_dates_stop_mattering_once_done()
    {
        var s = await SceneAsync(factory);
        var late = await CreateTaskAsync(s.Board, s.EventId, s.Committee, dueAt: DateTimeOffset.UtcNow.AddDays(-2));
        var card = (await BoardAsync(s.Board, s.EventId)).Card(late);
        Assert.True(card.GetProperty("overdue").GetBoolean());
        Assert.False(card.GetProperty("dueSoon").GetBoolean());

        await AssignOkAsync(s.Board, late, s.Lead.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, late)).StatusCode);
        var done = (await BoardAsync(s.Board, s.EventId)).Card(late);
        Assert.False(done.GetProperty("overdue").GetBoolean());
    }

    [Fact]
    public async Task References_are_per_event_and_handed_out_in_order()
    {
        var s = await SceneAsync(factory);
        var first = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var second = await CreateTaskAsync(s.Board, s.EventId, s.OtherCommittee);
        Assert.Equal("HTN-1", (await DetailAsync(s.Board, first)).GetProperty("reference").GetString());
        Assert.Equal("HTN-2", (await DetailAsync(s.Board, second)).GetProperty("reference").GetString());

        // A second event starts its own run at 1.
        var otherEvent = await OnSaleAsync(s.Board);
        var committee = await Committees.CommitteeTestData.AddCommitteeAsync(s.Board, otherEvent, "Logistics");
        var elsewhere = await CreateTaskAsync(s.Board, otherEvent, committee);
        Assert.Equal("HTN-1", (await DetailAsync(s.Board, elsewhere)).GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Filters_narrow_the_board_by_committee_mine_and_archive()
    {
        var s = await SceneAsync(factory);
        var mine = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Mine");
        await AssignOkAsync(s.Board, mine, s.Worker.PersonId, "Collab");
        var theirs = await CreateTaskAsync(s.Board, s.EventId, s.OtherCommittee, "Theirs");

        var filtered = await BoardAsync(s.Board, s.EventId, $"?committeeId={s.Committee}");
        Assert.Equal(["Mine"], filtered.CardTitles("Backlog"));

        var onlyMine = await BoardAsync(s.WorkerClient, s.EventId, "?mine=true");
        Assert.Equal(["Mine"], onlyMine.CardTitles("Backlog"));

        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/tasks/{theirs}/archive", new { reason = "Duplicate card." })).StatusCode);
        Assert.Equal(["Mine"], (await BoardAsync(s.Board, s.EventId)).CardTitles("Backlog"));
        Assert.Equal(2, (await BoardAsync(s.Board, s.EventId, "?includeArchived=true")).Column("Backlog").Cards().Count);

        // Restoring puts it back, and both actions are on the activity log.
        Assert.Equal(HttpStatusCode.OK, (await s.Board.PostAsync($"/api/tasks/{theirs}/restore")).StatusCode);
        Assert.Contains("restored", (await DetailAsync(s.Board, theirs)).ActivityKinds());
    }

    [Fact]
    public async Task My_tasks_lists_the_callers_open_cards_across_events()
    {
        var s = await SceneAsync(factory);
        var open = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Print ID lanyards", basePoints: 20);
        await AssignOkAsync(s.Board, open, s.Worker.PersonId, "Assistant");
        var finished = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Book venue", basePoints: 50);
        await AssignOkAsync(s.Board, finished, s.Worker.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, finished)).StatusCode);
        var notMine = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Someone else's");

        var res = await s.WorkerClient.GetAsync("/api/tasks/mine");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var mine = await ApiClient.JsonAsync(res);
        Assert.Equal(1, mine.GetProperty("openCount").GetInt32());
        var tasks = mine.GetProperty("groups").EnumerateArray().Single().GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(["Print ID lanyards"], tasks.Select(t => t.GetProperty("title").GetString()).ToList());
        Assert.DoesNotContain(notMine.ToString(), tasks.Select(t => t.GetProperty("id").GetString()));

        // Somebody with no tasks gets an empty list, not a 403.
        var empty = await ApiClient.JsonAsync(await s.OutsiderClient.GetAsync("/api/tasks/mine"));
        Assert.Equal(0, empty.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task Create_validates_its_body()
    {
        var s = await SceneAsync(factory);
        async Task<HttpResponseMessage> PostAsync(object body) => await s.Board.PostAsync($"/api/events/{s.EventId}/tasks", body);

        var empty = await PostAsync(NewTask(s.Committee, title: ""));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.True((await ApiClient.JsonAsync(empty)).HasError("title"));

        var points = await PostAsync(NewTask(s.Committee, basePoints: 2000));
        Assert.True((await ApiClient.JsonAsync(points)).HasError("basePoints"));

        var labels = await PostAsync(NewTask(s.Committee, labels: ["a", "b", "c", "d", "e", "f"]));
        Assert.True((await ApiClient.JsonAsync(labels)).HasError("labels"));

        var done = await PostAsync(NewTask(s.Committee, status: "Done"));
        Assert.True((await ApiClient.JsonAsync(done)).HasError("status"));

        // A committee of another event is a 404, never a cross-event write.
        var otherEvent = await OnSaleAsync(s.Board);
        var elsewhere = await Committees.CommitteeTestData.AddCommitteeAsync(s.Board, otherEvent, "Logistics");
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(NewTask(elsewhere))).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_committee_takes_no_new_tasks()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/events/{s.EventId}/committees/{s.OtherCommittee}/deactivate",
                new { reason = "Merged into Logistics." })).StatusCode);
        var refused = await s.Board.PostAsync($"/api/events/{s.EventId}/tasks", NewTask(s.OtherCommittee));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("committee_inactive", (await ApiClient.JsonAsync(refused)).Code());
    }

    // ---------- 403 matrix ----------

    [Fact]
    public async Task An_apprentice_on_no_committee_is_refused_everywhere()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var c = s.OutsiderClient;

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/events/{s.EventId}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/tasks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/events/{s.EventId}/tasks", NewTask(s.Committee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync($"/api/tasks/{id}", NewTask(s.Committee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(c, id, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(c, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReopenAsync(c, id, "No.")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/tasks/{id}/archive", new { reason = "No." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/tasks/{id}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(c, id, s.Outsider.PersonId, "Lead")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.DeleteAsync($"/api/tasks/{id}/assignees/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/tasks/{id}/checklist", new { text = "No." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PutAsync($"/api/tasks/{id}/checklist/{Guid.NewGuid()}", new { isDone = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.DeleteAsync($"/api/tasks/{id}/checklist/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsync($"/api/tasks/{id}/checklist/reorder", new { itemIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/tasks/{id}/comments", new { body = "No." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PutAsync($"/api/tasks/{id}/comments/{Guid.NewGuid()}", new { body = "No." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.DeleteAsync($"/api/tasks/{id}/comments/{Guid.NewGuid()}", new { reason = "No." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostFileAsync($"/api/tasks/{id}/attachments", Png, "a.png", "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/tasks/{id}/attachments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsync($"/api/tasks/{id}/attachments/{Guid.NewGuid()}/remove")).StatusCode);
    }

    [Fact]
    public async Task A_goon_who_leads_nothing_cannot_see_or_manage_the_board()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var (_, goon) = await Committees.CommitteeTestData.SignedInUserAsync(factory, PersonLevel.Goon);

        Assert.Equal(HttpStatusCode.Forbidden, (await goon.GetAsync($"/api/events/{s.EventId}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await goon.GetAsync($"/api/tasks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await goon.PostAsync($"/api/events/{s.EventId}/tasks", NewTask(s.Committee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(goon, id, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(goon, id)).StatusCode);
    }

    [Fact]
    public async Task A_lead_manages_only_their_own_committees_cards()
    {
        var s = await SceneAsync(factory);
        var mine = await CreateTaskAsync(s.LeadClient, s.EventId, s.Committee, "Mine");
        var theirs = await CreateTaskAsync(s.OtherLeadClient, s.EventId, s.OtherCommittee, "Theirs");

        // Creating in someone else's committee is refused, with the forbidden code the UI knows.
        var denied = await s.LeadClient.PostAsync($"/api/events/{s.EventId}/tasks", NewTask(s.OtherCommittee));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("forbidden", (await ApiClient.JsonAsync(denied)).Code());

        // Their card: readable (sibling committee of the same event), not writable.
        Assert.Equal(HttpStatusCode.OK, (await s.LeadClient.GetAsync($"/api/tasks/{theirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(s.LeadClient, theirs, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(s.LeadClient, theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(s.LeadClient, theirs, s.Lead.PersonId, "Lead")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.LeadClient.PostAsync($"/api/tasks/{theirs}/archive", new { reason = "Mine now." })).StatusCode);

        // Their own card: the full set.
        await AssignOkAsync(s.LeadClient, mine, s.Worker.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.LeadClient, mine, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.LeadClient, mine)).StatusCode);

        // Handing a card to another committee needs the right on the target too.
        var handover = await CreateTaskAsync(s.LeadClient, s.EventId, s.Committee, "Handover");
        var refused = await s.LeadClient.PutAsync($"/api/tasks/{handover}",
            new { title = "Handover", committeeId = s.OtherCommittee });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await s.Board.PutAsync($"/api/tasks/{handover}", new { title = "Handover", committeeId = s.OtherCommittee })).StatusCode);
        Assert.Equal(s.OtherCommittee, (await DetailAsync(s.Board, handover)).GetProperty("committeeId").GetGuid());
    }

    [Fact]
    public async Task An_assignee_moves_their_own_card_but_cannot_score_or_staff_it()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        // Her own card moves as far as Review.
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.WorkerClient, id, "InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(s.WorkerClient, id, "Review")).StatusCode);

        // Points, assignees, approving, reopening and archiving are Tasks.Manage.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.WorkerClient.PutAsync($"/api/tasks/{id}", new { title = "Mine", basePoints = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(s.WorkerClient, id, s.Outsider.PersonId, "Collab")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(s.WorkerClient, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReopenAsync(s.WorkerClient, id, "Mine.")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.WorkerClient.PostAsync($"/api/tasks/{id}/archive", new { reason = "Mine." })).StatusCode);
        Assert.Equal(20, (await DetailAsync(s.Board, id)).GetProperty("basePoints").GetInt32());

        // A card she isn't on, in her own committee, is read-only for her.
        var notHers = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Not hers");
        Assert.Equal(HttpStatusCode.OK, (await s.WorkerClient.GetAsync($"/api/tasks/{notHers}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(s.WorkerClient, notHers, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.WorkerClient.PostAsync($"/api/tasks/{notHers}/checklist", new { text = "No." })).StatusCode);

        var detail = await DetailAsync(s.WorkerClient, id);
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.True(detail.GetProperty("canWork").GetBoolean());
        Assert.False(detail.GetProperty("canApprove").GetBoolean());
        Assert.True(detail.GetProperty("canComment").GetBoolean());
    }

    [Fact]
    public async Task Someone_from_another_event_sees_nothing()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);

        // A lead of a committee on a different event.
        var otherEvent = await OnSaleAsync(s.Board);
        var committee = await Committees.CommitteeTestData.AddCommitteeAsync(s.Board, otherEvent, "Logistics");
        var (stranger, strangerClient) = await Committees.CommitteeTestData.SignedInUserAsync(factory, PersonLevel.Goon);
        await Committees.CommitteeTestData.SetLeadAsync(s.Board, otherEvent, committee, stranger.PersonId);

        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/events/{s.EventId}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/tasks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(strangerClient, id)).StatusCode);
        // And their own event's board stays empty of our cards.
        Assert.Empty((await BoardAsync(strangerClient, otherEvent)).Column("Backlog").Cards());
    }
}
