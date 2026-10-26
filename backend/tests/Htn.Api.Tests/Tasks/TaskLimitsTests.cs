using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Tasks.TaskTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The limits the Phase 4 review asked for: the attachment cap counts every upload ever (an upload → remove loop is
/// not unlimited storage), the Done column is capped and says so, and Draft events stay invisible in "My tasks".
/// </summary>
public sealed class TaskLimitsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task The_attachment_cap_counts_removed_uploads_too()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);

        // Five uploads, all removed again: the bytes are kept for ever, so they still count against the cap.
        for (var i = 0; i < 5; i++)
        {
            var uploaded = await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, $"shot-{i}.png", "image/png");
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            var attachmentId = (await ApiClient.JsonAsync(uploaded)).GetProperty("attachments").EnumerateArray()
                .Last().GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK,
                (await s.LeadClient.PostAsync($"/api/tasks/{id}/attachments/{attachmentId}/remove")).StatusCode);
        }
        Assert.Empty((await DetailAsync(s.Board, id)).GetProperty("attachments").EnumerateArray());

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, $"live-{i}.png", "image/png")).StatusCode);

        var refused = await s.LeadClient.PostFileAsync($"/api/tasks/{id}/attachments", Png, "eleven.png", "image/png");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("too_many_attachments", (await ApiClient.JsonAsync(refused)).Code());
        Assert.Equal(10, await factory.DbAsync(db => db.TaskAttachments.AsNoTracking().CountAsync(a => a.TaskId == id)));
    }

    [Fact]
    public async Task The_done_column_returns_the_newest_fifty_cards_and_says_there_are_more()
    {
        var s = await SceneAsync(factory);
        // A finished event's Done pile only grows, so it is seeded straight into the database: 55 approved cards.
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tasks (id, event_id, committee_id, number, reference, title, status, base_points, labels,
                               sort_order, created_at, approved_at, is_archived)
            SELECT gen_random_uuid(), {s.EventId}, {s.Committee}, 1000 + g, 'HTN-' || (1000 + g)::text,
                   'Done card ' || g, 'Done', 0, ARRAY[]::text[], g,
                   now() - (g || ' minutes')::interval, now() - (g || ' minutes')::interval, false
            FROM generate_series(1, 55) g
            """));
        var open = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Still open");

        var board = await BoardAsync(s.Board, s.EventId);
        var done = board.Column("Done");
        Assert.Equal(55, done.GetProperty("count").GetInt32());
        Assert.True(done.GetProperty("hasMore").GetBoolean());
        var cards = done.Cards();
        Assert.Equal(50, cards.Count);
        // Newest first, so the oldest five fall off the end.
        Assert.Equal("Done card 1", cards[0].GetProperty("title").GetString());
        Assert.Equal("Done card 50", cards[^1].GetProperty("title").GetString());

        // Every other column is complete, so nothing is hidden there.
        var backlog = board.Column("Backlog");
        Assert.False(backlog.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, backlog.GetProperty("count").GetInt32());
        Assert.Equal(open, backlog.Cards().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task My_tasks_never_names_a_draft_event()
    {
        var s = await SceneAsync(factory);
        // A Draft event, invisible to anyone without Events.Manage, with a card assigned to the apprentice.
        var draft = (await CreateAsync(s.Board)).Id();
        var draftCommittee = await AddCommitteeAsync(s.Board, draft, "Secret " + Guid.NewGuid().ToString("N")[..6]);
        var secret = await CreateTaskAsync(s.Board, draft, draftCommittee, "Unannounced keynote");
        await AssignOkAsync(s.Board, secret, s.Worker.PersonId, "Collab");
        var mine = await CreateTaskAsync(s.Board, s.EventId, s.Committee, "Print ID lanyards");
        await AssignOkAsync(s.Board, mine, s.Worker.PersonId, "Collab");

        var hers = await ApiClient.JsonAsync(await s.WorkerClient.GetAsync("/api/tasks/mine"));
        Assert.Equal(1, hers.GetProperty("openCount").GetInt32());
        var titles = hers.GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("tasks").EnumerateArray())
            .Select(t => t.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Print ID lanyards"], titles);
        // Not the event name either: the whole group is gone.
        Assert.DoesNotContain(draft.ToString(), hers.ToString(), StringComparison.OrdinalIgnoreCase);

        // The board, who may manage events, sees both.
        var boardView = await ApiClient.JsonAsync(await s.Board.GetAsync("/api/tasks/mine"));
        Assert.Equal(0, boardView.GetProperty("openCount").GetInt32()); // the board member is on no card
        var (boardAssignee, boardClient) = await SignedInUserAsync(factory, PersonLevel.Board);
        await AssignOkAsync(s.Board, secret, boardAssignee.PersonId, "Collab");
        var boardMine = await ApiClient.JsonAsync(await boardClient.GetAsync("/api/tasks/mine"));
        Assert.Equal(1, boardMine.GetProperty("openCount").GetInt32());
    }
}

/// <summary>Low task-write limit, so the per-user window is reached in a test.</summary>
public sealed class TaskWriteRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:TasksWritePermitLimit"] = "10",
        ["RateLimiting:TasksWriteWindowSeconds"] = "600",
        ["RateLimiting:MentionEmailsPerAuthorPermitLimit"] = "100000",
    };
}

public sealed class TaskWriteRateLimitTests(TaskWriteRateLimitApiFactory factory) : IClassFixture<TaskWriteRateLimitApiFactory>
{
    [Fact]
    public async Task Task_writes_are_limited_per_user_and_reads_are_not()
    {
        var s = await SceneAsync(factory);
        // The scene already spent writes on the board's bucket, so the limit is measured on a fresh user: the
        // committee lead, who may work her own card.
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await s.LeadClient.PostAsync($"/api/tasks/{id}/checklist", new { text = $"Step {i}" })).StatusCode);

        var limited = await s.LeadClient.PostAsync($"/api/tasks/{id}/checklist", new { text = "One too many" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).Code());
        // One bucket for every mutating task route, so a different write is refused too.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await MoveAsync(s.LeadClient, id, "Review")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ApproveAsync(s.LeadClient, id)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await s.LeadClient.PostAsync($"/api/tasks/{id}/comments", new { body = "Hello" })).StatusCode);

        // Reading the board and the card is never limited...
        Assert.Equal(HttpStatusCode.OK, (await s.LeadClient.GetAsync($"/api/tasks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.LeadClient.GetAsync($"/api/events/{s.EventId}/tasks")).StatusCode);
        // ...and another signed-in user has their own window.
        Assert.Equal(HttpStatusCode.Created,
            (await s.OtherLeadClient.PostAsync($"/api/events/{s.EventId}/tasks", NewTask(s.OtherCommittee))).StatusCode);
    }

    [Fact]
    public async Task Committee_writes_share_the_same_per_user_window()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        // The event and its tickets are not task routes; these ten committee writes are.
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Created,
                (await board.PostAsync($"/api/events/{eventId}/committees", NewCommittee("C" + i))).StatusCode);
        var limited = await board.PostAsync($"/api/events/{eventId}/committees", NewCommittee("C11"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).Code());
        // Volunteers are on the same bucket; reading the page is not.
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await board.GetAsync($"/api/events/{eventId}/committees")).StatusCode);
    }
}

/// <summary>Tiny @mention email budget, so the overflow is visible in a test.</summary>
public sealed class MentionEmailLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:MentionEmailsPerAuthorPermitLimit"] = "1",
        ["RateLimiting:MentionEmailsPerAuthorWindowSeconds"] = "3600",
    };
}

public sealed class MentionEmailLimitTests(MentionEmailLimitApiFactory factory) : IClassFixture<MentionEmailLimitApiFactory>
{
    [Fact]
    public async Task One_author_can_only_trigger_so_many_mention_emails_and_the_overflow_is_audited()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);

        var posted = await s.Board.PostAsync($"/api/tasks/{id}/comments", new
        {
            body = "Both of you, please look at this.",
            mentionPersonIds = new[] { s.Lead.PersonId, s.Worker.PersonId },
        });
        // The comment itself always goes through: only the email is dropped.
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var comment = (await ApiClient.JsonAsync(posted)).GetProperty("comments").EnumerateArray().Single();
        Assert.Equal(2, comment.GetProperty("mentions").GetArrayLength());

        var queued = await factory.DbAsync(db => db.Outbox.AsNoTracking()
            .Where(m => m.Type == EmailTypes.TaskMention).Select(m => m.PayloadJson).ToListAsync());
        Assert.Single(queued, p => p.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

        // The audit entry records what was mailed and how much the budget dropped.
        var entry = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == "task.mentions_notified" && a.EntityId == comment.GetProperty("id").GetGuid().ToString())
            .Select(a => a.AfterJson).SingleAsync());
        Assert.Contains("\"rateLimited\":1", entry!.Replace(" ", ""));
    }

    [Fact]
    public async Task A_mention_has_to_name_a_real_person()
    {
        var s = await SceneAsync(factory);
        var id = await CreateTaskAsync(s.Board, s.EventId, s.Committee);
        var refused = await s.Board.PostAsync($"/api/tasks/{id}/comments",
            new { body = "Who?", mentionPersonIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True((await ApiClient.JsonAsync(refused)).HasError("mentionPersonIds"));
        Assert.Empty(await factory.DbAsync(db => db.TaskComments.AsNoTracking().Where(c => c.TaskId == id).ToListAsync()));
    }
}
