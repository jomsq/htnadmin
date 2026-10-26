using System.Net;
using System.Text;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

public sealed class AdminRegistrationsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>Registers one checkout; <paramref name="attendees"/> gets the buyer's (returned) email.</summary>
    private async Task<(string Pid, Guid RegId, string Email)> RegisterOneAsync(OnSaleEvent ev, Func<string, object[]>? attendees = null)
    {
        var email = HtnApiFactory.UniqueEmail("adm");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, attendees?.Invoke(email) ?? [Person(email)]));
        var pid = created.GetProperty("participantIds")[0].GetString()!;
        return (pid, await RegistrationIdAsync(factory, pid), email);
    }

    [Fact]
    public async Task Goons_and_apprentices_are_forbidden_and_anonymous_is_unauthorized()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var (_, regId, _) = await RegisterOneAsync(ev);
        var basePath = $"/api/events/{ev.Id}/registrations";
        (HttpMethod Method, string Url, object? Body)[] routes =
        [
            (HttpMethod.Get, basePath, null),
            (HttpMethod.Get, $"{basePath}/{regId}", null),
            (HttpMethod.Get, $"{basePath}/{regId}/proof", null),
            (HttpMethod.Get, $"{basePath}/export.csv", null),
            (HttpMethod.Post, basePath, new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true }),
            (HttpMethod.Post, $"{basePath}/mark-paid", new { ids = new[] { regId }, method = "Cash" }),
            (HttpMethod.Post, $"{basePath}/{regId}/cancel", new { reason = "x" }),
            (HttpMethod.Post, $"{basePath}/{regId}/revert-payment", new { reason = "x" }),
            (HttpMethod.Post, $"{basePath}/{regId}/resend-billing", null),
            (HttpMethod.Post, $"{basePath}/{regId}/resend-ticket", null),
            (HttpMethod.Get, $"/api/events/{ev.Id}/email-settings", null),
            (HttpMethod.Put, $"/api/events/{ev.Id}/email-settings", new { accentColor = "#0F766E", paymentAccountIds = Array.Empty<Guid>() }),
            (HttpMethod.Post, $"/api/events/{ev.Id}/email-settings/test", new { template = "billing" }),
        ];
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            foreach (var (method, url, body) in routes)
                Assert.True(HttpStatusCode.Forbidden == (await client.SendAsync(method, url, body, csrf: true)).StatusCode, $"{level} {method} {url}");
        }
        var anon = factory.NewClient();
        foreach (var (method, url, body) in routes)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(method, url, body, csrf: true)).StatusCode);

        Assert.Equal(RegistrationStatus.PendingPayment,
            await factory.DbAsync(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.Status).SingleAsync()));
    }

    [Fact]
    public async Task List_has_counts_tabs_search_and_overdue()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var maria = await RegisterOneAsync(ev, email => [Person(email, first: "Maria", last: "Santos")]);
        var jose = await RegisterOneAsync(ev, email => [Person(email, first: "Jose", last: "Dela Cruz")]);
        // Names can't contain "_" any more; rows from before that rule still exist, and search must treat it literally.
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attendees SET last_name = 'Dela_Cruz' WHERE registration_id = {jose.RegId}"));
        var ana = await RegisterOneAsync(ev, email => [Person(email, first: "Ana", last: "Reyes")]);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new { ids = new[] { ana.RegId }, method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{jose.RegId}/cancel", new { reason = "Asked" })).StatusCode);
        // Maria's deadline passes.
        await factory.MakeOverdueAsync(maria.RegId);

        async Task<JsonElement> ListAsync(string query = "") =>
            await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations{query}"));

        var all = await ListAsync();
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        var counts = all.GetProperty("counts");
        Assert.Equal(3, counts.GetProperty("all").GetInt32());
        Assert.Equal(1, counts.GetProperty("pendingPayment").GetInt32());
        Assert.Equal(1, counts.GetProperty("paid").GetInt32());
        Assert.Equal(0, counts.GetProperty("checkedIn").GetInt32());
        Assert.Equal(1, counts.GetProperty("cancelled").GetInt32());
        Assert.Equal(1, counts.GetProperty("overdue").GetInt32());

        var row = all.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == maria.RegId);
        Assert.Equal("Maria Santos", row.GetProperty("buyerName").GetString());
        Assert.Equal(maria.Email, row.GetProperty("buyerEmail").GetString()); // Board only: not masked
        Assert.Equal("Regular", row.GetProperty("ticketName").GetString());
        Assert.True(row.GetProperty("overdue").GetBoolean());
        Assert.Equal("Public", row.GetProperty("source").GetString());
        Assert.Equal([maria.Pid], row.GetProperty("participantIds").Strings());
        var paidRow = all.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == ana.RegId);
        Assert.Equal("Cash", paidRow.GetProperty("method").GetString());

        var paid = await ListAsync("?status=Paid");
        Assert.Equal(ana.RegId, Assert.Single(paid.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(3, paid.GetProperty("counts").GetProperty("all").GetInt32());
        var overdue = await ListAsync("?overdue=true");
        Assert.Equal(maria.RegId, Assert.Single(overdue.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await board.GetAsync($"/api/events/{ev.Id}/registrations?status=Overdue")).StatusCode);

        // Search: name, participant ID, email; wildcards are literal.
        Assert.Equal(maria.RegId, (await ListAsync("?search=maria%20sant")).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, (await ListAsync($"?search={maria.Pid.ToLowerInvariant()}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await ListAsync($"?search={Uri.EscapeDataString(ana.Email.ToUpperInvariant())}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await ListAsync("?search=a_C")).GetProperty("total").GetInt32()); // only "Dela_Cruz"
        Assert.Equal(0, (await ListAsync("?search=%25")).GetProperty("total").GetInt32());
        Assert.Equal(jose.RegId, Assert.Single((await ListAsync("?search=_")).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(1, (await ListAsync("?search=reyes")).GetProperty("counts").GetProperty("all").GetInt32());

        // Paging.
        var page = await ListAsync("?page=2&pageSize=2");
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(100, (await ListAsync("?pageSize=1000")).GetProperty("pageSize").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/events/{Guid.NewGuid()}/registrations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/events/{Guid.NewGuid()}/registrations/{maria.RegId}")).StatusCode);
    }

    [Fact]
    public async Task Mark_paid_queues_ticket_emails_with_an_inline_qr_and_calendar_file()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, ticket: Ticket("Team of 3", 4500m, 10, groupSize: 3));
        var buyer = HtnApiFactory.UniqueEmail("buyer");
        var member = HtnApiFactory.UniqueEmail("member");
        // Manual registration: an admin may enter a member under the buyer's address (the public form needs distinct ones).
        var manual = await board.PostAsync($"/api/events/{ev.Id}/registrations", new
        {
            ticketTypeId = ev.TicketId, consent = true,
            attendees = new object[] { Person(buyer), Member(member, "Jo", "Reyes"), Member(buyer, "Kid", "Santos") },
        });
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var r = (RegId: (await ApiClient.JsonAsync(manual)).Id(), Email: buyer);
        var single = await PublishedAsync(board);
        var other = await RegisterOneAsync(single);
        await factory.DrainOutboxAsync();

        var account = await ApiClient.JsonAsync(await board.PostAsync("/api/settings/payment-accounts",
            new { kind = "GCash", label = "Main GCash", accountName = "HackTheNorth.PH Inc.", accountNumber = "0917 000 1234" }));
        var res = await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new
        {
            ids = new[] { r.RegId, other.RegId, Guid.NewGuid() }, method = "GCash", paymentAccountId = account.Id(), reference = "7741 0923",
            receivedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, (await ApiClient.JsonAsync(res)).GetProperty("updated").GetInt32()); // other event's row is skipped

        var payment = await factory.DbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.RegistrationId == r.RegId));
        Assert.Equal(4500m, payment.Amount);
        Assert.Equal(PaymentMethod.GCash, payment.Method);
        Assert.Equal(account.Id(), payment.PaymentAccountId);
        Assert.Equal("7741 0923", payment.Reference);
        Assert.NotNull(payment.MarkedById);
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "registration.mark_paid" && a.EntityId == r.RegId.ToString())));

        // Marking again does nothing.
        Assert.Equal(0, (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { r.RegId }, method = "Cash" }))).GetProperty("updated").GetInt32());

        await factory.DrainOutboxAsync();
        // Buyer: own ticket + Kid's (same address) + a copy of Jo's. Jo: own ticket.
        var buyerMails = factory.Emails.To(buyer).Where(m => m.Category == EmailCategories.Ticket).ToList();
        var memberMails = factory.Emails.To(member).Where(m => m.Category == EmailCategories.Ticket).ToList();
        Assert.Equal(3, buyerMails.Count);
        Assert.Single(memberMails);
        Assert.Contains(buyerMails, m => m.Html.Contains("Jo Reyes", StringComparison.Ordinal) && m.Html.Contains("in your group", StringComparison.Ordinal));

        var mail = memberMails.Single();
        Assert.Contains("cid:" + RegistrationEmails.QrContentId, mail.Html, StringComparison.Ordinal);
        var qr = Assert.Single(mail.AllAttachments, a => a.ContentId == RegistrationEmails.QrContentId);
        Assert.True(qr.IsInline);
        Assert.Equal("image/png", qr.ContentType);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], qr.Content[..4]);
        var ics = Assert.Single(mail.AllAttachments, a => a.ContentType == "text/calendar");
        Assert.False(ics.IsInline);
        var icsText = Encoding.UTF8.GetString(ics.Content);
        Assert.Contains("BEGIN:VEVENT\r\n", icsText, StringComparison.Ordinal);
        Assert.Contains("LOCATION:SMX Convention Center\\, Pasay", icsText, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:", icsText, StringComparison.Ordinal);
        var jo = await factory.DbAsync(db => db.Attendees.AsNoTracking().SingleAsync(a => a.RegistrationId == r.RegId && a.SortOrder == 1));
        Assert.Contains(jo.ParticipantId, mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revert_payment_invalidates_the_old_qr_and_drops_unsent_tickets()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var r = await RegisterOneAsync(ev);
        await factory.DrainOutboxAsync();
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new { ids = new[] { r.RegId }, method = "Cash" })).StatusCode);
        var attendee = await factory.DbAsync(db => db.Attendees.AsNoTracking().SingleAsync(a => a.RegistrationId == r.RegId));
        var tokens = factory.Services.GetRequiredService<ITicketToken>();
        var oldToken = tokens.Issue(ev.Id, attendee.Id, attendee.TicketVersion);

        var noReason = await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/revert-payment", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var res = await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/revert-payment", new { reason = "Payment bounced" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        Assert.Equal("PendingPayment", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("payment").ValueKind);
        Assert.Contains(detail.GetProperty("history").EnumerateArray(), h => h.GetProperty("kind").GetString() == "revert_payment"
            && h.GetProperty("reason").GetString() == "Payment bounced" && h.GetProperty("actorName").GetString() is not null);

        // The old QR still has a valid signature but names a ticket version that no longer exists.
        Assert.True(tokens.TryVerify(oldToken, out var payload));
        var now = await factory.DbAsync(db => db.Attendees.AsNoTracking().SingleAsync(a => a.Id == attendee.Id));
        Assert.Equal(attendee.TicketVersion + 1, now.TicketVersion);
        Assert.NotEqual(now.TicketVersion, payload.TicketVersion);
        Assert.NotEqual(oldToken, tokens.Issue(ev.Id, attendee.Id, now.TicketVersion));

        // The ticket email queued by mark-paid was not sent yet: it is dropped instead of delivering a dead QR.
        await factory.DrainOutboxAsync();
        Assert.DoesNotContain(factory.Emails.To(r.Email), m => m.Category == EmailCategories.Ticket);

        var payments = await factory.DbAsync(db => db.Payments.AsNoTracking().Where(p => p.RegistrationId == r.RegId).ToListAsync());
        Assert.Equal("Payment bounced", Assert.Single(payments).RevertReason);

        // Paid again: a new payment row, the new version goes out.
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new { ids = new[] { r.RegId }, method = "Cash" })).StatusCode);
        await factory.DrainOutboxAsync();
        Assert.Single(factory.Emails.To(r.Email), m => m.Category == EmailCategories.Ticket);
        Assert.Equal(2, await factory.DbAsync(db => db.Payments.CountAsync(p => p.RegistrationId == r.RegId)));

        // Revert is only for paid registrations.
        var pending = await RegisterOneAsync(ev);
        var wrong = await board.PostAsync($"/api/events/{ev.Id}/registrations/{pending.RegId}/revert-payment", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
    }

    [Fact]
    public async Task Cancel_frees_the_seat_notifies_the_buyer_and_is_final()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 1);
        var r = await RegisterOneAsync(ev);
        var full = await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person()]));
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/cancel", new { reason = " " })).StatusCode);
        var res = await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/cancel", new { reason = "Asked to cancel" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
        Assert.Equal("Asked to cancel", detail.GetProperty("cancelReason").GetString());
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "registration.cancel" && a.EntityId == r.RegId.ToString()));
        Assert.Equal("Asked to cancel", audit.Reason);

        await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person()]));
        await factory.DrainOutboxAsync();
        Assert.Single(factory.Emails.To(r.Email), m => m.Subject.Contains("cancelled", StringComparison.Ordinal));

        var again = await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/cancel", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/resend-billing")).StatusCode);
        Assert.Equal(0, (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { r.RegId }, method = "Cash" }))).GetProperty("updated").GetInt32());
        // Never deleted.
        Assert.True(await factory.DbAsync(db => db.Registrations.AnyAsync(x => x.Id == r.RegId)));
    }

    [Fact]
    public async Task Manual_registration_allows_hidden_comps_and_marking_paid()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var comp = (await AddTicketAsync(board, ev.Id, Ticket("Sponsor comp", 0m, 5, visibility: "Hidden"))).Id();
        var email = HtnApiFactory.UniqueEmail("comp");

        var res = await board.PostAsync($"/api/events/{ev.Id}/registrations", new { ticketTypeId = comp, attendees = new[] { Person(email) }, consent = true });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        Assert.Equal("Paid", detail.GetProperty("status").GetString());
        Assert.Equal("Manual", detail.GetProperty("source").GetString());
        Assert.Equal("Comp", detail.GetProperty("payment").GetProperty("method").GetString());
        Assert.Equal("Sponsor comp", detail.GetProperty("ticketName").GetString());

        var paidNow = await board.PostAsync($"/api/events/{ev.Id}/registrations", new
        {
            ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true,
            markPaid = new { method = "Cash", reference = "OR 0012" },
        });
        Assert.Equal(HttpStatusCode.Created, paidNow.StatusCode);
        var paidDetail = await ApiClient.JsonAsync(paidNow);
        Assert.Equal("Paid", paidDetail.GetProperty("status").GetString());
        Assert.Equal(1500m, paidDetail.GetProperty("payment").GetProperty("amount").GetDecimal());
        Assert.Equal(["create", "mark_paid"], paidDetail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("kind").GetString()!));

        var pending = await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/registrations",
            new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true }));
        Assert.Equal("PendingPayment", pending.GetProperty("status").GetString());

        // Same validation as the public form; consent still required.
        var noConsent = await board.PostAsync($"/api/events/{ev.Id}/registrations", new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = false });
        Assert.True((await ApiClient.JsonAsync(noConsent)).HasError("consent"));
        var badAccount = await board.PostAsync($"/api/events/{ev.Id}/registrations", new
        {
            ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true, markPaid = new { method = "GCash", paymentAccountId = Guid.NewGuid() },
        });
        Assert.Equal("invalid_payment_account", (await ApiClient.JsonAsync(badAccount)).Code());

        // Turned off for the event.
        await factory.DbAsync(db => db.Events.Where(e => e.Id == ev.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.ManualRegistration, false)));
        var off = await board.PostAsync($"/api/events/{ev.Id}/registrations", new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true });
        Assert.Equal("registration_closed", (await ApiClient.JsonAsync(off)).Code());
    }

    [Fact]
    public async Task Resend_billing_and_ticket_follow_the_status()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var r = await RegisterOneAsync(ev);
        await factory.DrainOutboxAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/resend-ticket")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/resend-billing")).StatusCode);
        await factory.DrainOutboxAsync();
        Assert.Equal(2, factory.Emails.To(r.Email).Count(m => m.Category == EmailCategories.Billing));

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new { ids = new[] { r.RegId }, method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/resend-billing")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{r.RegId}/resend-ticket")).StatusCode);
        await factory.DrainOutboxAsync();
        Assert.Equal(2, factory.Emails.To(r.Email).Count(m => m.Category == EmailCategories.Ticket));
        var kinds = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations/{r.RegId}"))).GetProperty("history")
            .EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["create", "resend_billing", "mark_paid", "resend_ticket"], kinds);
    }

    [Fact]
    public async Task Csv_export_escapes_formulas_and_quotes()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var r = await RegisterOneAsync(ev, email => [Person(email, first: "Maria", last: "Santos Jr.", company: "DICT")]);
        // The form refuses these now; older rows (or a future import) could still hold them, so the export must escape.
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attendees SET first_name = {"=HYPERLINK(\"http://x\")"}, last_name = {"Santos, Jr."}, company = {"@SUM(A1)"} WHERE registration_id = {r.RegId}"));

        var res = await board.GetAsync($"/api/events/{ev.Id}/registrations/export.csv");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains(".csv", res.Content.Headers.ContentDisposition?.FileName ?? "", StringComparison.Ordinal);
        var csv = Encoding.UTF8.GetString(await res.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Participant ID,First name,", lines[0], StringComparison.Ordinal);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(r.Pid + ",\"'=HYPERLINK(\"\"http://x\"\")\",,\"Santos, Jr.\",", lines[1], StringComparison.Ordinal);
        Assert.Contains(",'@SUM(A1),", lines[1], StringComparison.Ordinal);
        Assert.Contains(",'+639171234567,", lines[1], StringComparison.Ordinal); // a leading + is a formula trigger too
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "registration.export" && a.EntityId == ev.Id.ToString())));
    }
}
