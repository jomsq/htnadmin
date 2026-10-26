using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Events;

public sealed class EventsEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static List<JsonElement> Items(JsonElement array) => array.EnumerateArray().ToList();

    private static async Task<List<Guid>> ListIdsAsync(ApiClient client, string tab)
    {
        var res = await client.GetAsync($"/api/events?tab={tab}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return Items(await ApiClient.JsonAsync(res)).Select(e => e.Id()).ToList();
    }

    [Fact]
    public async Task Goon_and_apprentice_can_view_but_not_change_events()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = await OnSaleAsync(board);
        var ticketId = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{id}"))).GetProperty("tickets")[0].Id();
        var voucherRes = await board.PostAsync($"/api/events/{id}/vouchers", Voucher(code: "VIEWONLY"));
        var voucherId = (await ApiClient.JsonAsync(voucherRes)).Id();

        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/events/{id}")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/events", NewEvent())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/events/{id}", NewEvent())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/publish")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/close")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostFileAsync($"/api/events/{id}/cover", Settings.OrgSettingsEndpointsTests.Png, "c.png", "image/png")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/tickets", Ticket())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/events/{id}/tickets/{ticketId}", Ticket())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/tickets/{ticketId}/deactivate")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/vouchers", Voucher(code: "NOPE1"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/events/{id}/vouchers/{voucherId}", Voucher(code: "NOPE1"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{id}/vouchers/{voucherId}/deactivate")).StatusCode);
        }

        var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/events/{id}")).StatusCode);

        // Nothing changed.
        var ev = await factory.DbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Id == id));
        Assert.Equal(EventStatus.OnSale, ev.Status);
        Assert.Null(ev.CoverFileId);
    }

    [Fact]
    public async Task Drafts_are_hidden_from_non_managers()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var draft = (await CreateAsync(board)).Id();
        var published = await OnSaleAsync(board);

        Assert.Contains(draft, await ListIdsAsync(board, "drafts"));
        Assert.Contains(draft, await ListIdsAsync(board, "all"));
        Assert.Equal(HttpStatusCode.OK, (await board.GetAsync($"/api/events/{draft}")).StatusCode);

        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Empty(await ListIdsAsync(client, "drafts"));
            var all = await ListIdsAsync(client, "all");
            Assert.DoesNotContain(draft, all);
            Assert.Contains(published, all);
            Assert.Contains(published, await ListIdsAsync(client, "upcoming"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/events/{draft}")).StatusCode);

            // Published details: tickets yes, vouchers never.
            var detail = await ApiClient.JsonAsync(await client.GetAsync($"/api/events/{published}"));
            Assert.Equal(1, detail.GetProperty("tickets").GetArrayLength());
            Assert.Equal(0, detail.GetProperty("vouchers").GetArrayLength());
        }
    }

    [Fact]
    public async Task Create_returns_a_draft_with_a_generated_slug_and_audits()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var name = "Hack Night: Cloud " + Guid.NewGuid().ToString("N")[..6];
        var res = await board.PostAsync("/api/events", NewEvent(name: name));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var e = await ApiClient.JsonAsync(res);
        var slug = e.GetProperty("slug").GetString()!;
        Assert.Equal(EventSlug.FromName(name), slug);
        Assert.Equal("Draft", e.GetProperty("status").GetString());
        Assert.Equal(0, e.GetProperty("sold").GetInt32());
        Assert.Equal(400, e.GetProperty("capacity").GetInt32());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("coverUrl").ValueKind);
        Assert.False(e.GetProperty("isMainEvent").GetBoolean());
        Assert.True(e.GetProperty("manualRegistration").GetBoolean());
        Assert.Equal("Upload", e.GetProperty("proofMode").GetString());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("membershipFee").ValueKind);
        Assert.Equal(0, e.GetProperty("tickets").GetArrayLength());
        Assert.Equal($"/api/events/{e.Id()}", res.Headers.Location?.ToString());

        // Same name again: the next free slug.
        var second = await CreateAsync(board, NewEvent(name: name));
        Assert.Equal(slug + "-2", second.GetProperty("slug").GetString());
        var third = await CreateAsync(board, NewEvent(name: name));
        Assert.Equal(slug + "-3", third.GetProperty("slug").GetString());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "event.create" && a.EntityId == e.Id().ToString()));
        Assert.Contains(slug, audit.AfterJson, StringComparison.Ordinal);
        Assert.NotNull(audit.ActorId);
    }

    [Fact]
    public async Task Slug_rules_and_duplicates()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var slug = "my-event-" + Guid.NewGuid().ToString("N")[..6];
        var created = await CreateAsync(board, NewEvent(slug: slug));
        Assert.Equal(slug, created.GetProperty("slug").GetString());

        var dup = await board.PostAsync("/api/events", NewEvent(slug: slug));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_slug", (await ApiClient.JsonAsync(dup)).Code());

        foreach (var bad in new[] { "ab", "Has-Caps", "two--dashes", "-lead", "trail-", "under_score", new string('a', 61) })
        {
            var res = await board.PostAsync("/api/events", NewEvent(slug: bad));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await ApiClient.JsonAsync(res)).HasError("slug"), bad);
        }

        // Rename to another event's slug is a conflict too.
        var other = await CreateAsync(board);
        var rename = await board.PutAsync($"/api/events/{other.Id()}", NewEvent(slug: slug));
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        Assert.Equal("duplicate_slug", (await ApiClient.JsonAsync(rename)).Code());
    }

    [Fact]
    public async Task Create_validates_fields()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var badDates = await board.PostAsync("/api/events", NewEvent(startsAt: Start, endsAt: Start.AddHours(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, badDates.StatusCode);
        Assert.True((await ApiClient.JsonAsync(badDates)).HasError("endsAt"));

        foreach (var capacity in new[] { 0, 100_001 })
        {
            var res = await board.PostAsync("/api/events", NewEvent(capacity: capacity));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await ApiClient.JsonAsync(res)).HasError("capacity"));
        }

        var missing = await board.PostAsync("/api/events", new { name = "", venue = "" });
        var errors = await ApiClient.JsonAsync(missing);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        foreach (var field in new[] { "name", "venue", "startsAt", "endsAt", "capacity" }) Assert.True(errors.HasError(field), field);
    }

    [Fact]
    public async Task Update_changes_details_settings_and_locks_slug_after_publish()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var newSlug = "renamed-" + Guid.NewGuid().ToString("N")[..6];
        var res = await board.PutAsync($"/api/events/{id}", new
        {
            name = "HTN Con 2026", slug = newSlug, startsAt = Start, endsAt = Start.AddHours(8), venue = "PICC, Pasay", capacity = 500,
            description = (string?)null, manualRegistration = false, groupRegistration = false, publicPage = false, proofMode = "ReplyEmail",
            isMainEvent = true, membershipFee = 300m, membershipYear = 2031,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var e = await ApiClient.JsonAsync(res);
        Assert.Equal(newSlug, e.GetProperty("slug").GetString());
        Assert.Equal("PICC, Pasay", e.GetProperty("venue").GetString());
        Assert.Equal(500, e.GetProperty("capacity").GetInt32());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("description").ValueKind);
        Assert.False(e.GetProperty("manualRegistration").GetBoolean());
        Assert.False(e.GetProperty("publicPage").GetBoolean());
        Assert.Equal("ReplyEmail", e.GetProperty("proofMode").GetString());
        Assert.True(e.GetProperty("isMainEvent").GetBoolean());
        Assert.Equal(300m, e.GetProperty("membershipFee").GetDecimal());
        Assert.Equal(2031, e.GetProperty("membershipYear").GetInt32());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "event.update" && a.EntityId == id.ToString()));
        Assert.Contains("SMX", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("PICC", audit.AfterJson, StringComparison.Ordinal);

        // A second main event for the same year is refused.
        var other = (await CreateAsync(board)).Id();
        var clash = await board.PutAsync($"/api/events/{other}", new
        {
            name = "Other", startsAt = Start, endsAt = Start.AddHours(8), venue = "V", capacity = 10, isMainEvent = true,
            membershipFee = 300m, membershipYear = 2031,
        });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("duplicate_main_event", (await ApiClient.JsonAsync(clash)).Code());

        // Main event without fee/year.
        var noFee = await board.PutAsync($"/api/events/{other}", new
        {
            name = "Other", startsAt = Start, endsAt = Start.AddHours(8), venue = "V", capacity = 10, isMainEvent = true, membershipYear = 2032,
        });
        Assert.Equal(HttpStatusCode.BadRequest, noFee.StatusCode);
        Assert.True((await ApiClient.JsonAsync(noFee)).HasError("membershipFee"));
        var noYear = await board.PutAsync($"/api/events/{other}", new
        {
            name = "Other", startsAt = Start, endsAt = Start.AddHours(8), venue = "V", capacity = 10, isMainEvent = true, membershipFee = 0m,
        });
        Assert.True((await ApiClient.JsonAsync(noYear)).HasError("membershipYear"));

        // Turning it off clears fee and year.
        var off = await ApiClient.JsonAsync(await board.PutAsync($"/api/events/{id}", new
        {
            name = "HTN Con 2026", startsAt = Start, endsAt = Start.AddHours(8), venue = "PICC, Pasay", capacity = 500, isMainEvent = false,
        }));
        Assert.False(off.GetProperty("isMainEvent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("membershipFee").ValueKind);
        Assert.Equal(newSlug, off.GetProperty("slug").GetString()); // omitted slug is kept
        Assert.Equal("ReplyEmail", off.GetProperty("proofMode").GetString()); // omitted settings are kept

        // After publishing, the slug is locked; keeping it is fine.
        await AddTicketAsync(board, id);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{id}/publish")).StatusCode);
        var locked = await board.PutAsync($"/api/events/{id}", NewEvent(slug: newSlug + "-x"));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("slug_locked", (await ApiClient.JsonAsync(locked)).Code());
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/events/{id}", NewEvent(slug: newSlug))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await board.PutAsync($"/api/events/{Guid.NewGuid()}", NewEvent())).StatusCode);
    }

    [Fact]
    public async Task Publish_needs_an_active_ticket_and_close_reopens()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();

        var noTickets = await board.PostAsync($"/api/events/{id}/publish");
        Assert.Equal(HttpStatusCode.Conflict, noTickets.StatusCode);
        Assert.Equal("no_tickets", (await ApiClient.JsonAsync(noTickets)).Code());

        // A deactivated ticket doesn't count.
        var ticket = (await AddTicketAsync(board, id)).Id();
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{id}/tickets/{ticket}/deactivate")).StatusCode);
        Assert.Equal("no_tickets", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{id}/publish"))).Code());

        await AddTicketAsync(board, id);
        var published = await board.PostAsync($"/api/events/{id}/publish");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("OnSale", (await ApiClient.JsonAsync(published)).GetProperty("status").GetString());

        var again = await board.PostAsync($"/api/events/{id}/publish");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invalid_status", (await ApiClient.JsonAsync(again)).Code());

        var closed = await board.PostAsync($"/api/events/{id}/close");
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal("Closed", (await ApiClient.JsonAsync(closed)).GetProperty("status").GetString());
        Assert.Equal("invalid_status", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{id}/close"))).Code());

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{id}/publish")).StatusCode);

        var actions = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == id.ToString() && a.EntityType == "Event").OrderBy(a => a.At).Select(a => a.Action).ToListAsync());
        Assert.Equal(["event.create", "event.publish", "event.close", "event.publish"], actions);

        var draft = (await CreateAsync(board)).Id();
        Assert.Equal("invalid_status", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{draft}/close"))).Code());
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{Guid.NewGuid()}/publish")).StatusCode);
    }

    [Fact]
    public async Task Past_events_show_as_ended_in_the_past_tab_newest_first()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var now = DateTimeOffset.UtcNow;

        // Events that have already ended can't be published through the API, so insert them directly.
        async Task<Guid> PastAsync(int daysAgo, bool close)
        {
            var e = Event.Create("Past " + daysAgo, "past-" + Guid.NewGuid().ToString("N")[..10],
                now.AddDays(-daysAgo), now.AddDays(-daysAgo).AddHours(8), "BGC, Taguig", 120, null, now.AddDays(-daysAgo - 30), null);
            e.Publish(true, now.AddDays(-daysAgo - 20), null);
            if (close) e.Close(now.AddDays(-daysAgo - 1), null);
            await factory.DbAsync(async db => { db.Events.Add(e); return await db.SaveChangesAsync(); });
            return e.Id;
        }
        var older = await PastAsync(60, close: true);
        var recent = await PastAsync(10, close: false);

        var past = await ListIdsAsync(board, "past");
        Assert.True(past.IndexOf(recent) < past.IndexOf(older));
        Assert.DoesNotContain(recent, await ListIdsAsync(board, "upcoming"));

        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        var detail = await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{older}"));
        Assert.Equal("Ended", detail.GetProperty("status").GetString());

        var ended = await board.PostAsync($"/api/events/{older}/publish");
        Assert.Equal(HttpStatusCode.Conflict, ended.StatusCode); // no tickets on these, so check with one
        await AddTicketAsync(board, older);
        Assert.Equal("event_ended", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{older}/publish"))).Code());

        // Upcoming is ordered by start date.
        var later = await OnSaleAsync(board, NewEvent(startsAt: now.AddDays(90)));
        var sooner = await OnSaleAsync(board, NewEvent(startsAt: now.AddDays(5)));
        var upcoming = await ListIdsAsync(board, "upcoming");
        Assert.True(upcoming.IndexOf(sooner) < upcoming.IndexOf(later));

        var summary = Items(await ApiClient.JsonAsync(await goon.GetAsync("/api/events?tab=upcoming"))).Single(e => e.Id() == sooner);
        Assert.Equal("OnSale", summary.GetProperty("status").GetString());
        foreach (var field in new[] { "slug", "name", "startsAt", "endsAt", "venue", "capacity", "sold", "coverUrl", "isMainEvent" })
            Assert.True(summary.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Unknown_tab_is_a_400_and_default_is_upcoming()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var bad = await board.GetAsync("/api/events?tab=archived");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True((await ApiClient.JsonAsync(bad)).HasError("tab"));

        var id = await OnSaleAsync(board);
        Assert.Contains(id, Items(await ApiClient.JsonAsync(await board.GetAsync("/api/events"))).Select(e => e.Id()));
        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/events/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Board_without_2fa_is_gated_from_events()
    {
        var strict = new Board2faApiFactory();
        await strict.InitializeAsync();
        try
        {
            var board = await strict.SignedInAsync(PersonLevel.Board);
            var res = await board.GetAsync("/api/events");
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal("2fa_required", (await ApiClient.JsonAsync(res)).Code());
        }
        finally
        {
            await ((IAsyncLifetime)strict).DisposeAsync();
        }
    }
}
