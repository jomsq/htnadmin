using System.Net;
using Htn.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The database guards added by the Phase 4 review: the cross-event keys, the frozen assignees of a finished card,
/// the committee-lead agreement, and the extra bounds on labels, reversals and the task counter. Every one of them is
/// tried from raw SQL, because that is the only way past the API's own checks.
/// </summary>
public sealed class TaskGuards2Tests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string message)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_child_can_never_belong_to_another_event()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var otherEvent = await OnSaleAsync(s.Board);
        var otherCommittee = await AddCommitteeAsync(s.Board, otherEvent, "Logistics");
        var strangerVolunteer = (await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/events/{otherEvent}/volunteers",
            new { name = "Pia Santos" }))).GetProperty("id").GetGuid();

        // An assignee row whose event doesn't match its task.
        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, event_id, person_id, role, added_at) VALUES (gen_random_uuid(), {id}, {otherEvent}, {s.Worker.PersonId}, 'Collab', now())",
            "fk_task_assignees_tasks_task_id_event_id");
        // A volunteer of another event, even with the task's own event on the row.
        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, event_id, volunteer_id, role, added_at) VALUES (gen_random_uuid(), {id}, {s.EventId}, {strangerVolunteer}, 'Assistant', now())",
            "fk_task_assignees_event_volunteers_volunteer_id_event_id");
        // An award snapshot naming a task of one event and a committee of another.
        await AssertRejectedAsync(
            $"INSERT INTO points_awards (id, task_id, event_id, committee_id, person_id, role, base_points, multiplier, points, awarded_at) VALUES (gen_random_uuid(), {id}, {s.EventId}, {otherCommittee}, {s.Worker.PersonId}, 'Lead', 10, 1, 10, now())",
            "fk_points_awards_committees_committee_id_event_id");
        await AssertRejectedAsync(
            $"INSERT INTO points_awards (id, task_id, event_id, committee_id, person_id, role, base_points, multiplier, points, awarded_at) VALUES (gen_random_uuid(), {id}, {otherEvent}, {otherCommittee}, {s.Worker.PersonId}, 'Lead', 10, 1, 10, now())",
            "fk_points_awards_tasks_task_id_event_id");
    }

    [Fact]
    public async Task The_assignees_of_a_done_or_archived_task_are_frozen_even_from_raw_sql()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        var assigneeId = await factory.DbAsync(db => db.TaskAssignees.AsNoTracking()
            .Where(a => a.TaskId == id).Select(a => a.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);

        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, event_id, person_id, role, added_at) VALUES (gen_random_uuid(), {id}, {s.EventId}, {s.Worker.PersonId}, 'Collab', now())",
            "frozen");
        await AssertRejectedAsync($"UPDATE task_assignees SET role = 'Assistant' WHERE id = {assigneeId}", "frozen");
        await AssertRejectedAsync($"DELETE FROM task_assignees WHERE id = {assigneeId}", "frozen");

        // Reopening unfreezes the card, archiving freezes it again.
        Assert.Equal(HttpStatusCode.OK, (await ReopenAsync(s.Board, id, "Not finished after all.")).StatusCode);
        Assert.Equal(1, await SqlAsync($"UPDATE task_assignees SET role = 'Collab' WHERE id = {assigneeId}"));
        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.PostAsync($"/api/tasks/{id}/archive", new { reason = "Dropped." })).StatusCode);
        await AssertRejectedAsync($"UPDATE task_assignees SET role = 'Lead' WHERE id = {assigneeId}", "frozen");
    }

    [Fact]
    public async Task The_lead_column_and_the_lead_membership_row_can_no_longer_drift_apart()
    {
        var s = await SceneAsync(factory);

        // The column alone: authorization would still read the old Lead row.
        await AssertRejectedAsync($"UPDATE committees SET lead_person_id = NULL WHERE id = {s.Committee}", "must agree");
        await AssertRejectedAsync(
            $"UPDATE committees SET lead_person_id = {s.Worker.PersonId} WHERE id = {s.Committee}", "must agree");
        // The membership row alone: the UI would still show the old lead.
        await AssertRejectedAsync(
            $"UPDATE committee_members SET role = 'Member' WHERE committee_id = {s.Committee} AND person_id = {s.Lead.PersonId} AND left_at IS NULL",
            "must agree");

        // Both together in one transaction is exactly what PUT /lead does, and is allowed.
        var page = await PageAsync(s.Board, s.EventId);
        Assert.Equal(s.Lead.PersonId, page.Committee(s.Committee).GetProperty("lead").GetProperty("personId").GetGuid());
    }

    [Fact]
    public async Task Labels_reversals_and_the_task_counter_are_bounded()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 10);

        await AssertRejectedAsync($"UPDATE tasks SET labels = ARRAY['']::text[] WHERE id = {id}", "ck_tasks_labels");
        await AssertRejectedAsync(
            $"UPDATE tasks SET labels = ARRAY[repeat('x', 35), repeat('y', 35), repeat('z', 35), repeat('a', 35), repeat('b', 35)]::text[] WHERE id = {id}",
            "ck_tasks_labels");
        Assert.Equal(1, await SqlAsync($"UPDATE tasks SET labels = ARRAY['Venue', 'Printing']::text[] WHERE id = {id}"));

        await AssertRejectedAsync($"UPDATE events SET task_counter = -1 WHERE id = {s.EventId}", "ck_events_task_counter");

        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        var awardId = await factory.DbAsync(db => db.PointsAwards.AsNoTracking()
            .Where(a => a.TaskId == id).Select(a => a.Id).SingleAsync());
        // A reversal can never predate the award it reverses.
        await AssertRejectedAsync(
            $"UPDATE points_awards SET reversed_at = awarded_at - interval '1 day', reversal_reason = 'backdated' WHERE id = {awardId}",
            "ck_points_awards_reversal");
    }

    [Fact]
    public async Task A_comment_keeps_its_author_after_the_column_was_renamed()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var commentId = (await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/tasks/{id}/comments", new { body = "Noted." })))
            .GetProperty("comments").EnumerateArray().Single().GetProperty("id").GetGuid();

        // The guard trigger was re-created against author_user_id, so it still refuses a rewritten author...
        await AssertRejectedAsync(
            $"UPDATE task_comments SET author_user_id = gen_random_uuid() WHERE id = {commentId}", "immutable");
        // ...and the column is now a real foreign key to users.
        await AssertRejectedAsync(
            $"INSERT INTO task_comments (id, task_id, author_user_id, body, mentioned_person_ids, created_at) VALUES (gen_random_uuid(), {id}, {s.Lead.PersonId}, 'Not a user id', '{{}}'::uuid[], now())",
            "fk_task_comments_users_author_user_id");
    }
}
