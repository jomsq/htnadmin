using System.Net;
using Htn.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The database guards behind the API's own rules: the activity log and the points awards are append-only, tasks,
/// comments and attachments are never deleted, and the per-event reference numbers hold under concurrency.
/// </summary>
public sealed class TaskIntegrityTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string message)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_activity_log_is_append_only_even_from_raw_sql()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var activityId = await factory.DbAsync(db => db.TaskActivities.AsNoTracking()
            .Where(a => a.TaskId == id).Select(a => a.Id).FirstAsync());

        await AssertRejectedAsync($"UPDATE task_activities SET text = 'tampered' WHERE id = {activityId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM task_activities WHERE id = {activityId}", "append-only");
        await AssertRejectedAsync($"TRUNCATE task_activities", "append-only");
        Assert.True(await factory.DbAsync(db => db.TaskActivities.AsNoTracking().AnyAsync(a => a.Id == activityId)));
    }

    [Fact]
    public async Task Points_awards_can_only_ever_be_reversed_once_and_never_rewritten()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);
        var awardId = await factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => a.TaskId == id)
            .Select(a => a.Id).SingleAsync());

        await AssertRejectedAsync($"DELETE FROM points_awards WHERE id = {awardId}", "never deleted");
        await AssertRejectedAsync($"UPDATE points_awards SET points = 999 WHERE id = {awardId}", "snapshot");
        await AssertRejectedAsync($"UPDATE points_awards SET multiplier = 2, points = 80 WHERE id = {awardId}", "snapshot");
        await AssertRejectedAsync($"UPDATE points_awards SET person_id = gen_random_uuid() WHERE id = {awardId}", "snapshot");
        // points must equal base × multiplier.
        await AssertRejectedAsync(
            $"INSERT INTO points_awards (id, task_id, event_id, committee_id, person_id, role, base_points, multiplier, points, awarded_at) VALUES (gen_random_uuid(), {id}, {s.EventId}, {s.Committee}, {s.Worker.PersonId}, 'Lead', 40, 1, 999, now())",
            "ck_points_awards_points");

        // Reversing through the API is fine; a second change to the reversed row is not.
        Assert.Equal(HttpStatusCode.OK, (await ReopenAsync(s.Board, id, "Wrong card.")).StatusCode);
        await AssertRejectedAsync($"UPDATE points_awards SET reversal_reason = 'other' WHERE id = {awardId}", "cannot change again");
    }

    [Fact]
    public async Task Tasks_comments_and_attachments_are_never_deleted_and_keep_their_reference()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var commentId = (await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/tasks/{id}/comments", new { body = "Noted." })))
            .GetProperty("comments").EnumerateArray().Single().GetProperty("id").GetGuid();
        var attachmentId = (await ApiClient.JsonAsync(
                await s.Board.PostFileAsync($"/api/tasks/{id}/attachments", Pdf, "plan.pdf", "application/pdf")))
            .GetProperty("attachments").EnumerateArray().Single().GetProperty("id").GetGuid();

        await AssertRejectedAsync($"DELETE FROM tasks WHERE id = {id}", "never deleted");
        await AssertRejectedAsync($"TRUNCATE tasks CASCADE", "append-only");
        await AssertRejectedAsync($"UPDATE tasks SET number = 999, reference = 'HTN-999' WHERE id = {id}", "immutable");
        await AssertRejectedAsync($"UPDATE tasks SET reference = 'HTN-999' WHERE id = {id}", "immutable");
        await AssertRejectedAsync($"DELETE FROM task_comments WHERE id = {commentId}", "never removed");
        await AssertRejectedAsync($"UPDATE task_comments SET author_id = gen_random_uuid() WHERE id = {commentId}", "immutable");
        await AssertRejectedAsync($"DELETE FROM task_attachments WHERE id = {attachmentId}", "never deleted");
        await AssertRejectedAsync($"UPDATE task_attachments SET file_name = 'other.pdf' WHERE id = {attachmentId}", "removal stamp");

        // The reference must always match the number, and a status/approval mismatch is refused.
        await AssertRejectedAsync($"UPDATE tasks SET status = 'Done' WHERE id = {id}", "ck_tasks_approved");
        await AssertRejectedAsync($"UPDATE tasks SET base_points = 5000 WHERE id = {id}", "ck_tasks_base_points");
        await AssertRejectedAsync($"UPDATE tasks SET labels = '{{a,b,c,d,e,f}}' WHERE id = {id}", "ck_tasks_labels");
        Assert.Equal(1, await SqlAsync($"UPDATE tasks SET title = 'Still editable' WHERE id = {id}"));
    }

    [Fact]
    public async Task A_volunteer_assignee_can_never_be_a_lead_and_never_both_person_and_volunteer()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var volunteer = (await ApiClient.JsonAsync(await s.Board.PostAsync($"/api/events/{s.EventId}/volunteers",
            new { name = "Pia Santos" }))).GetProperty("id").GetGuid();

        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, volunteer_id, role, added_at) VALUES (gen_random_uuid(), {id}, {volunteer}, 'Lead', now())",
            "ck_task_assignees_volunteer_role");
        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, person_id, volunteer_id, role, added_at) VALUES (gen_random_uuid(), {id}, {s.Lead.PersonId}, {volunteer}, 'Assistant', now())",
            "ck_task_assignees_who");
        await AssertRejectedAsync(
            $"INSERT INTO task_assignees (id, task_id, role, added_at) VALUES (gen_random_uuid(), {id}, 'Lead', now())",
            "ck_task_assignees_who");
    }

    [Fact]
    public async Task Parallel_creates_hand_out_unique_reference_numbers()
    {
        var board = await factory.SignedInAsync(Htn.Domain.Organization.PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");

        // Six admins creating a card at the same moment: the counter on the event row is bumped under its lock.
        var clients = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => factory.SignedInAsync(Htn.Domain.Organization.PersonLevel.Board)));
        var results = await Task.WhenAll(clients.Select(c =>
            c.PostAsync($"/api/events/{eventId}/tasks", NewTask(committee, "Parallel " + Guid.NewGuid().ToString("N")[..6]))));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var numbers = await factory.DbAsync(db => db.Tasks.AsNoTracking().Where(t => t.EventId == eventId)
            .Select(t => t.Number).ToListAsync());
        Assert.Equal(6, numbers.Count);
        Assert.Equal(Enumerable.Range(1, 6), numbers.Order());
        var references = await factory.DbAsync(db => db.Tasks.AsNoTracking().Where(t => t.EventId == eventId)
            .Select(t => t.Reference).ToListAsync());
        Assert.Equal(6, references.Distinct().Count());
    }

    [Fact]
    public async Task A_second_live_award_for_the_same_person_is_impossible()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 20);
        await AssignOkAsync(s.Board, id, s.Lead.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(s.Board, id)).StatusCode);

        // The unique partial index is the backstop behind the idempotent approve.
        await AssertRejectedAsync(
            $"INSERT INTO points_awards (id, task_id, event_id, committee_id, person_id, role, base_points, multiplier, points, awarded_at) VALUES (gen_random_uuid(), {id}, {s.EventId}, {s.Committee}, {s.Lead.PersonId}, 'Lead', 20, 1, 20, now())",
            "ux_points_awards_live_task_person");
    }
}
