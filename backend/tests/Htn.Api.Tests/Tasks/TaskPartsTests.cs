using System.Net;
using System.Text;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>Assignees, checklist, comments with @mentions and attachments — the task detail dialog of frame 08.</summary>
public sealed class TaskPartsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    // ---------- Assignees ----------

    [Fact]
    public async Task Assignees_come_and_go_with_their_role_and_the_activity_log()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee, basePoints: 40);

        var added = await AssignAsync(s.LeadClient, id, s.Worker.PersonId, "Collab");
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var detail = await ApiClient.JsonAsync(added);
        var assignee = detail.GetProperty("assignees").EnumerateArray().Single();
        Assert.Equal("Collab", assignee.GetProperty("role").GetString());
        Assert.Equal(0.70m, assignee.GetProperty("multiplier").GetDecimal());
        Assert.Equal(28.00m, assignee.GetProperty("points").GetDecimal());
        Assert.False(assignee.GetProperty("isVolunteer").GetBoolean());
        Assert.Contains("assignee_added", detail.ActivityKinds());
        Assert.NotNull(detail.GetProperty("assignedByName").GetString());

        // The same person twice is a conflict.
        var again = await AssignAsync(s.LeadClient, id, s.Worker.PersonId, "Lead");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_assigned", (await ApiClient.JsonAsync(again)).Code());

        // A roster-only member (no login) can't be assigned.
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        var refused = await AssignAsync(s.LeadClient, id, member.PersonId, "Assistant");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("wrong_level", (await ApiClient.JsonAsync(refused)).Code());

        // Either a person or a volunteer, never both and never neither.
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(s.LeadClient, id, null, "Lead")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AssignAsync(s.LeadClient, id, s.Lead.PersonId, "Assistant", Guid.NewGuid())).StatusCode);

        var assigneeId = assignee.GetProperty("id").GetGuid();
        var removed = await s.LeadClient.DeleteAsync($"/api/tasks/{id}/assignees/{assigneeId}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var after = await ApiClient.JsonAsync(removed);
        Assert.Empty(after.GetProperty("assignees").EnumerateArray());
        Assert.Contains("assignee_removed", after.ActivityKinds());
        Assert.Equal(HttpStatusCode.NotFound, (await s.LeadClient.DeleteAsync($"/api/tasks/{id}/assignees/{assigneeId}")).StatusCode);
    }

    // ---------- Checklist ----------

    [Fact]
    public async Task The_checklist_is_added_ticked_reordered_and_removed_by_anyone_working_the_card()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var ids = new List<Guid>();
        foreach (var text in new[] { "Get SMX CAD file", "Place registration desks", "Mark exits" })
        {
            var res = await s.WorkerClient.PostAsync($"/api/tasks/{id}/checklist", new { text });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            ids.Add((await ApiClient.JsonAsync(res)).GetProperty("checklist").EnumerateArray().Last().GetProperty("id").GetGuid());
        }

        var ticked = await s.WorkerClient.PutAsync($"/api/tasks/{id}/checklist/{ids[0]}", new { isDone = true });
        Assert.Equal(HttpStatusCode.OK, ticked.StatusCode);
        Assert.Contains("checklist_toggled", (await ApiClient.JsonAsync(ticked)).ActivityKinds());
        var card = (await BoardAsync(s.Board, s.EventId)).Card(id);
        Assert.Equal(1, card.GetProperty("checklistDone").GetInt32());
        Assert.Equal(3, card.GetProperty("checklistTotal").GetInt32());

        var renamed = await s.WorkerClient.PutAsync($"/api/tasks/{id}/checklist/{ids[1]}", new { text = "Place 12 sponsor booths" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var reordered = await s.WorkerClient.PostAsync($"/api/tasks/{id}/checklist/reorder",
            new { itemIds = new[] { ids[2], ids[1], ids[0] } });
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal([ids[2], ids[1], ids[0]],
            (await ApiClient.JsonAsync(reordered)).GetProperty("checklist").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid()).ToList());

        // An unknown step, and a step from another task, are both refused.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.WorkerClient.PostAsync($"/api/tasks/{id}/checklist/reorder", new { itemIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.WorkerClient.DeleteAsync($"/api/tasks/{id}/checklist/{Guid.NewGuid()}")).StatusCode);

        var removed = await s.WorkerClient.DeleteAsync($"/api/tasks/{id}/checklist/{ids[0]}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var detail = await ApiClient.JsonAsync(removed);
        Assert.Equal(2, detail.GetProperty("checklist").GetArrayLength());
        Assert.Contains("checklist_removed", detail.ActivityKinds());

        var empty = await s.WorkerClient.PostAsync($"/api/tasks/{id}/checklist", new { text = "" });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.True((await ApiClient.JsonAsync(empty)).HasError("text"));
    }

    // ---------- Comments and mentions ----------

    [Fact]
    public async Task A_comment_mentions_only_people_who_can_see_the_board()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var posted = await s.WorkerClient.PostAsync($"/api/tasks/{id}/comments", new
        {
            body = "Booth 7 blocks exit B, can we move it?",
            // The lead is on this event, a board member is an admin everywhere, the outsider is on no committee.
            mentionPersonIds = new[] { s.Lead.PersonId, s.Outsider.PersonId },
        });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var detail = await ApiClient.JsonAsync(posted);
        var comment = detail.GetProperty("comments").EnumerateArray().Single();
        Assert.Equal("Booth 7 blocks exit B, can we move it?", comment.GetProperty("body").GetString());
        Assert.Equal(2, comment.GetProperty("mentions").GetArrayLength());
        Assert.True(comment.GetProperty("canEdit").GetBoolean());
        Assert.Contains("comment_added", detail.ActivityKinds());
        // No email address is ever echoed back to the caller.
        Assert.DoesNotContain("@", detail.GetProperty("comments").ToString());

        // One outbox message, for the lead only; the outsider is dropped without a word.
        var queued = await factory.DbAsync(db => db.Outbox.AsNoTracking()
            .Where(m => m.Type == EmailTypes.TaskMention).Select(m => m.PayloadJson).ToListAsync());
        var mine = queued.Where(p => p.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(mine);
        Assert.Contains(s.Lead.PersonId.ToString(), mine[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(s.Outsider.PersonId.ToString(), mine[0], StringComparison.OrdinalIgnoreCase);

        // The worker sent it, so she is never mailed about her own comment.
        Assert.DoesNotContain(s.Worker.PersonId.ToString(), mine[0], StringComparison.OrdinalIgnoreCase);

        // The outbox renders and sends it with the branding layout.
        await factory.ProcessOutboxAsync();
        var email = factory.Emails.Sent.Last(m => m.Category == EmailCategories.TaskMention);
        Assert.Equal(s.Lead.Email, email.To);
        Assert.Contains("HTN-", email.Subject);
        Assert.Contains("Booth 7 blocks exit B", email.Html);
        Assert.Contains("/tasks/" + id, email.Html);
        // The comment is escaped, never injected into the HTML.
        Assert.DoesNotContain("<script", email.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_mention_email_is_not_sent_once_the_comment_is_deleted()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var posted = await s.LeadClient.PostAsync($"/api/tasks/{id}/comments", new
        {
            body = "<script>alert(1)</script> please check this",
            mentionPersonIds = new[] { s.Worker.PersonId },
        });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var commentId = (await ApiClient.JsonAsync(posted)).GetProperty("comments").EnumerateArray()
            .Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await s.LeadClient.DeleteAsync($"/api/tasks/{id}/comments/{commentId}", new { reason = "Wrong card." })).StatusCode);
        var before = factory.Emails.Sent.Count(m => m.Category == EmailCategories.TaskMention);
        await factory.ProcessOutboxAsync();
        Assert.Equal(before, factory.Emails.Sent.Count(m => m.Category == EmailCategories.TaskMention));
    }

    [Fact]
    public async Task A_mention_email_is_dropped_when_the_recipient_has_left_the_committee()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var posted = await s.LeadClient.PostAsync($"/api/tasks/{id}/comments", new
        {
            body = "Can you check the desk plan?",
            mentionPersonIds = new[] { s.Worker.PersonId },
        });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        // It was queued for her: she was on the committee when the comment was written.
        var queued = await factory.DbAsync(db => db.Outbox.AsNoTracking()
            .Where(m => m.Type == EmailTypes.TaskMention).Select(m => m.PayloadJson).ToListAsync());
        Assert.Contains(queued, p => p.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)
                                     && p.Contains(s.Worker.PersonId.ToString(), StringComparison.OrdinalIgnoreCase));

        // She leaves the committee before the outbox runs: the scope is checked again at send time, so no email.
        Assert.Equal(HttpStatusCode.NoContent,
            (await s.Board.DeleteAsync($"/api/events/{s.EventId}/committees/{s.Committee}/members/{s.Worker.PersonId}")).StatusCode);
        var before = factory.Emails.Sent.Count(m => m.To == s.Worker.Email);
        await factory.ProcessOutboxAsync();
        Assert.Equal(before, factory.Emails.Sent.Count(m => m.To == s.Worker.Email));
    }

    [Fact]
    public async Task Only_the_author_edits_a_comment_and_deleting_is_soft_with_a_reason()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");
        var posted = await s.WorkerClient.PostAsync($"/api/tasks/{id}/comments", new { body = "First look done." });
        var commentId = (await ApiClient.JsonAsync(posted)).GetProperty("comments").EnumerateArray()
            .Single().GetProperty("id").GetGuid();

        var notAuthor = await s.LeadClient.PutAsync($"/api/tasks/{id}/comments/{commentId}", new { body = "Mine now." });
        Assert.Equal(HttpStatusCode.Conflict, notAuthor.StatusCode);
        Assert.Equal("not_author", (await ApiClient.JsonAsync(notAuthor)).Code());

        var edited = await s.WorkerClient.PutAsync($"/api/tasks/{id}/comments/{commentId}", new { body = "First look done, two fixes left." });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var comment = (await ApiClient.JsonAsync(edited)).GetProperty("comments").EnumerateArray().Single();
        Assert.Equal("First look done, two fixes left.", comment.GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, comment.GetProperty("editedAt").ValueKind);

        // No reason, no delete.
        var noReason = await s.WorkerClient.DeleteAsync($"/api/tasks/{id}/comments/{commentId}", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        // The board may delete anyone's comment; the row stays, the text stops being served.
        var deleted = await s.Board.DeleteAsync($"/api/tasks/{id}/comments/{commentId}", new { reason = "Off topic." });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var gone = (await ApiClient.JsonAsync(deleted)).GetProperty("comments").EnumerateArray().Single();
        Assert.True(gone.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("", gone.GetProperty("body").GetString());
        Assert.False(gone.GetProperty("canEdit").GetBoolean());

        var row = await factory.DbAsync(db => db.TaskComments.AsNoTracking().SingleAsync(c => c.Id == commentId));
        Assert.Equal("Off topic.", row.DeleteReason);
        Assert.Equal("First look done, two fixes left.", row.Body); // kept for the audit trail
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.WorkerClient.PutAsync($"/api/tasks/{id}/comments/{commentId}", new { body = "Back." })).StatusCode);
    }

    // ---------- Attachments ----------

    [Fact]
    public async Task Attachments_accept_png_jpg_and_pdf_and_nothing_else()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        await AssignOkAsync(s.Board, id, s.Worker.PersonId, "Collab");

        var pdf = await s.WorkerClient.PostFileAsync($"/api/tasks/{id}/attachments", Pdf, "floorplan-v3.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var detail = await ApiClient.JsonAsync(pdf);
        var attachment = detail.GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("floorplan-v3.pdf", attachment.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        Assert.Equal($"/api/tasks/{id}/attachments/{attachment.GetProperty("id").GetGuid()}", attachment.GetProperty("url").GetString());
        Assert.Contains("attachment_added", detail.ActivityKinds());

        Assert.Equal(HttpStatusCode.OK,
            (await s.WorkerClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, "smx-power-map.png", "image/png")).StatusCode);

        // An HTML file dressed up as a PNG is refused: the bytes decide.
        var html = Encoding.ASCII.GetBytes("<html><body><script>alert(1)</script></body></html>");
        var refused = await s.WorkerClient.PostFileAsync($"/api/tasks/{id}/attachments", html, "evil.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("unsupported_type", (await ApiClient.JsonAsync(refused)).Code());

        // Over 5 MB never reaches storage.
        var tooBig = new byte[6 * 1024 * 1024];
        Png.CopyTo(tooBig, 0);
        var big = await s.WorkerClient.PostFileAsync($"/api/tasks/{id}/attachments", tooBig, "huge.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Equal("file_too_large", (await ApiClient.JsonAsync(big)).Code());

        Assert.Equal(2, (await DetailAsync(s.Board, id)).GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task An_attachment_is_served_as_a_download_to_board_viewers_only()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var uploaded = await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Pdf, "floorplan-v3.pdf", "application/pdf");
        var attachmentId = (await ApiClient.JsonAsync(uploaded)).GetProperty("attachments").EnumerateArray()
            .Single().GetProperty("id").GetGuid();
        var url = $"/api/tasks/{id}/attachments/{attachmentId}";

        // A member of a sibling committee of the same event may download it.
        var res = await s.OtherLeadClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("floorplan-v3.pdf", res.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString());
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(await res.Content.ReadAsByteArrayAsync())[..5]);

        // Nobody from outside the event.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.OutsiderClient.GetAsync(url)).StatusCode);

        // Removing it is a timestamp: the row stays and the download 404s.
        var removed = await s.LeadClient.PostAsync($"{url}/remove");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var detail = await ApiClient.JsonAsync(removed);
        Assert.Empty(detail.GetProperty("attachments").EnumerateArray());
        Assert.Contains("attachment_removed", detail.ActivityKinds());
        Assert.Equal(HttpStatusCode.NotFound, (await s.LeadClient.GetAsync(url)).StatusCode);
        Assert.True(await factory.DbAsync(db => db.TaskAttachments.AsNoTracking().AnyAsync(a => a.Id == attachmentId)));
    }

    [Fact]
    public async Task A_task_holds_at_most_ten_attachments()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, $"shot-{i}.png", "image/png")).StatusCode);

        var refused = await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, "shot-11.png", "image/png");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("too_many_attachments", (await ApiClient.JsonAsync(refused)).Code());
    }
}
