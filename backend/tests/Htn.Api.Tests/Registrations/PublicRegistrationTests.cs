using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

public sealed partial class PublicRegistrationTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [GeneratedRegex("^HTN-[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$")]
    private static partial Regex ParticipantIdPattern();

    [Fact]
    public async Task Public_page_shows_only_on_sale_public_tickets_and_form_options()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 10);
        await AddTicketAsync(board, ev.Id, Ticket("Sponsor comp", 0m, 30, visibility: "Hidden"));
        await AddTicketAsync(board, ev.Id, Ticket("Team of 5", 6000m, 10, groupSize: 5));
        await AddTicketAsync(board, ev.Id, Ticket("Later", 100m, 10, salesStartsAt: DateTimeOffset.UtcNow.AddDays(3)));

        var anon = factory.NewClient();
        var res = await anon.GetAsync($"/api/public/events/{ev.Slug}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = await ApiClient.JsonAsync(res);
        Assert.Equal(["Regular", "Team of 5"], page.GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("name").GetString()!));
        var regular = page.GetProperty("tickets")[0];
        Assert.Equal(10, regular.GetProperty("remaining").GetInt32()); // capacity 10 limits quantity 200
        Assert.Equal(2, page.GetProperty("tickets")[1].GetProperty("remaining").GetInt32()); // 10 seats / 5 per group
        Assert.Equal("Upload", page.GetProperty("proofMode").GetString());
        Assert.Equal("#B71C1C", page.GetProperty("brandColor").GetString());
        Assert.Equal(["XS", "S", "M", "L", "XL", "2XL"], page.GetProperty("shirtSizes").Strings());
        Assert.Contains("Female", page.GetProperty("genders").Strings());
        Assert.Contains("Friend or colleague", page.GetProperty("hearAbout").Strings());
        Assert.Equal(RegistrationForm.ConsentVersion, page.GetProperty("consentVersion").GetString());
        Assert.False(page.TryGetProperty("capacity", out _));

        // Group registration off hides group tickets.
        var off = await board.PutAsync($"/api/events/{ev.Id}", UpdateBody(groupRegistration: false));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        page = await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/events/{ev.Slug}"));
        Assert.Equal(["Regular"], page.GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("name").GetString()!));
    }

    private static object UpdateBody(bool? groupRegistration = null, bool? publicPage = null, string? proofMode = null,
        DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, int capacity = 10) => new
    {
        name = "Updated event", startsAt = startsAt ?? Start, endsAt = endsAt ?? (startsAt ?? Start).AddHours(10), venue = "SMX", capacity,
        groupRegistration, publicPage, proofMode,
    };

    [Fact]
    public async Task Draft_closed_hidden_and_unknown_events_are_404()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var anon = factory.NewClient();
        var draft = await CreateAsync(board);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/events/{draft.GetProperty("slug").GetString()}")).StatusCode);

        var ev = await PublishedAsync(board);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/events/{ev.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/events/{ev.Id}", UpdateBody(publicPage: false, capacity: 400))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/events/{ev.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()]))).StatusCode);

        var closed = await PublishedAsync(board);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{closed.Id}/close")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/events/{closed.Slug}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/events/no-such-event")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/events/NOT_A_SLUG")).StatusCode);
    }

    [Fact]
    public async Task Register_creates_a_pending_registration_and_queues_billing_with_an_upload_link()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var email = HtnApiFactory.UniqueEmail("maria");
        var anon = factory.NewClient();

        var res = await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email, mobile: "0917 123 4567")]));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await ApiClient.JsonAsync(res);
        var pid = Assert.Single(created.GetProperty("participantIds").Strings());
        Assert.Matches(ParticipantIdPattern(), pid);
        Assert.Equal(1500m, created.GetProperty("amountDue").GetDecimal());
        Assert.Equal("Upload", created.GetProperty("proofMode").GetString());
        Assert.Equal("PendingPayment", created.GetProperty("status").GetString());
        Assert.Equal(email, created.GetProperty("email").GetString());
        var deadline = created.GetProperty("deadline").GetDateTimeOffset();
        Assert.InRange(deadline, DateTimeOffset.UtcNow.AddHours(47), DateTimeOffset.UtcNow.AddHours(49));
        // Nothing else personal is echoed.
        Assert.False(created.TryGetProperty("attendees", out _));
        Assert.False(created.TryGetProperty("mobile", out _));

        var regId = await RegistrationIdAsync(factory, pid);
        var reg = await factory.DbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == regId));
        Assert.Equal(RegistrationSource.Public, reg.Source);
        Assert.Equal(RegistrationForm.ConsentVersion, reg.ConsentVersion);
        Assert.InRange(reg.ConsentAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal("+639171234567", reg.Attendees.Single().Mobile);
        Assert.NotNull(reg.ProofTokenHash);
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "registration.create" && a.EntityId == regId.ToString())));

        var billing = (await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == RegistrationEmailTypes.Billing).ToListAsync()))
            .Single(o => o.PayloadJson.Contains(regId.ToString(), StringComparison.Ordinal));
        Assert.NotNull(billing.Secret);
        Assert.DoesNotContain(billing.Secret!, billing.PayloadJson, StringComparison.Ordinal);

        await factory.DrainOutboxAsync();
        var mail = factory.Emails.To(email).Single();
        Assert.Equal(EmailCategories.Billing, mail.Category);
        Assert.Contains(pid, mail.Html, StringComparison.Ordinal);
        Assert.Contains(pid, mail.Text, StringComparison.Ordinal);
        Assert.Contains("₱1,500", mail.Text, StringComparison.Ordinal);
        Assert.Contains("https://admin.test/pay/", mail.Html, StringComparison.Ordinal);
        Assert.Equal(billing.Secret, factory.Emails.ProofToken(email));

        // The event's sold count now includes the seat.
        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"));
        Assert.Equal(1, detail.GetProperty("sold").GetInt32());
        Assert.Equal(1, detail.GetProperty("tickets")[0].GetProperty("sold").GetInt32());
    }

    [Fact]
    public async Task Reply_email_mode_has_no_upload_link()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/events/{ev.Id}", UpdateBody(proofMode: "ReplyEmail", capacity: 400))).StatusCode);
        var email = HtnApiFactory.UniqueEmail("reply");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        Assert.Equal("ReplyEmail", created.GetProperty("proofMode").GetString());
        await factory.DrainOutboxAsync();
        var mail = factory.Emails.To(email).Single();
        Assert.DoesNotContain("/pay/", mail.Html, StringComparison.Ordinal);
        Assert.Contains("reply to this email", mail.Text, StringComparison.OrdinalIgnoreCase);
        var regId = await RegistrationIdAsync(factory, created.GetProperty("participantIds")[0].GetString()!);
        Assert.Null(await factory.DbAsync(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.ProofTokenHash).SingleAsync()));
    }

    [Fact]
    public async Task Validation_errors_name_the_field()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, ticket: Ticket("Pair", 2000m, 10, groupSize: 2));
        var anon = factory.NewClient();
        (object Body, string Field)[] cases =
        [
            (Body(ev.TicketId, [Person(), Member()], consent: false), "consent"),
            (Body(ev.TicketId, [Person(mobile: "12345"), Member()]), "attendees[0].mobile"),
            (Body(ev.TicketId, [Person(mobile: null), Member()]), "attendees[0].mobile"),
            (Body(ev.TicketId, [Person(gender: "Robot"), Member()]), "attendees[0].gender"),
            (Body(ev.TicketId, [Person(email: "not-an-email"), Member()]), "attendees[0].email"),
            (Body(ev.TicketId, [Person(), new { firstName = "Jo", lastName = "Reyes" }]), "attendees[1].email"),
            (Body(ev.TicketId, [Person(), new { lastName = "Reyes", email = "x@example.ph" }]), "attendees[1].firstName"),
            (Body(ev.TicketId, []), "attendees"),
            (new { idempotencyKey = Guid.NewGuid(), attendees = new[] { Person(), Member() }, consent = true }, "ticketTypeId"),
            (new { ticketTypeId = ev.TicketId, attendees = new[] { Person(), Member() }, consent = true }, "idempotencyKey"),
        ];
        foreach (var (body, field) in cases)
        {
            var res = await RegisterAsync(anon, ev.Slug, body);
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{field}: {(int)res.StatusCode}");
            Assert.True((await ApiClient.JsonAsync(res)).HasError(field), field);
        }
        // Group members don't need gender or mobile.
        await RegisterOkAsync(anon, ev.Slug, Body(ev.TicketId, [Person(), Member()]));

        // Wrong number of attendees for the ticket.
        var mismatch = await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()]));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal("group_size", (await ApiClient.JsonAsync(mismatch)).Code());
    }

    [Fact]
    public async Task Honeypot_and_missing_csrf_store_nothing()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var anon = factory.NewClient();

        var bot = await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()], website: "http://spam.example"));
        Assert.Equal(HttpStatusCode.BadRequest, bot.StatusCode);
        var problem = await ApiClient.JsonAsync(bot);
        Assert.Equal("rejected", problem.Code());
        Assert.False(problem.TryGetProperty("errors", out _));

        var noCsrf = await anon.PostAsync($"/api/public/events/{ev.Slug}/registrations", Body(ev.TicketId, [Person()]), csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("antiforgery", (await ApiClient.JsonAsync(noCsrf)).Code());

        Assert.Equal(0, await factory.DbAsync(db => db.Registrations.CountAsync(r => r.EventId == ev.Id)));
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_original_response()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var anon = factory.NewClient();
        var key = Guid.NewGuid();
        var email = HtnApiFactory.UniqueEmail("dup");
        var first = await RegisterOkAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email)], key: key));
        var second = await RegisterOkAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email)], key: key));
        Assert.Equal(first.GetProperty("participantIds").Strings(), second.GetProperty("participantIds").Strings());
        Assert.Equal(first.GetProperty("deadline").GetDateTimeOffset(), second.GetProperty("deadline").GetDateTimeOffset());
        Assert.Equal(1, await factory.DbAsync(db => db.Registrations.CountAsync(r => r.EventId == ev.Id)));
        var regId = await factory.DbAsync(db => db.Registrations.Where(r => r.EventId == ev.Id).Select(r => r.Id).SingleAsync());
        Assert.Single(await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == RegistrationEmailTypes.Billing).ToListAsync()),
            o => o.PayloadJson.Contains(regId.ToString(), StringComparison.Ordinal));

        // The same key on another event is a new checkout.
        var other = await PublishedAsync(board);
        var third = await RegisterOkAsync(anon, other.Slug, Body(other.TicketId, [Person(email)], key: key));
        Assert.NotEqual(first.GetProperty("participantIds")[0].GetString(), third.GetProperty("participantIds")[0].GetString());
    }

    [Fact]
    public async Task Hidden_inactive_and_out_of_window_tickets_are_unavailable()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var anon = factory.NewClient();
        var hidden = (await AddTicketAsync(board, ev.Id, Ticket("Comp", 0m, 10, visibility: "Hidden"))).Id();
        var later = (await AddTicketAsync(board, ev.Id, Ticket("Later", 100m, 10, salesStartsAt: DateTimeOffset.UtcNow.AddDays(2)))).Id();
        var inactive = (await AddTicketAsync(board, ev.Id, Ticket("Old", 100m, 10))).Id();
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/tickets/{inactive}/deactivate")).StatusCode);
        var otherEvent = await PublishedAsync(board);

        foreach (var ticket in new[] { hidden, later, inactive, otherEvent.TicketId, Guid.NewGuid() })
        {
            var res = await RegisterAsync(anon, ev.Slug, Body(ticket, [Person()]));
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal("ticket_unavailable", (await ApiClient.JsonAsync(res)).Code());
        }

        // Group tickets need group registration on.
        var team = (await AddTicketAsync(board, ev.Id, Ticket("Team of 2", 2000m, 10, groupSize: 2))).Id();
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/events/{ev.Id}", UpdateBody(groupRegistration: false, capacity: 400))).StatusCode);
        var group = await RegisterAsync(anon, ev.Slug, Body(team, [Person(), Member()]));
        Assert.Equal("ticket_unavailable", (await ApiClient.JsonAsync(group)).Code());
    }

    [Fact]
    public async Task Group_checkout_creates_unique_participant_ids_and_counts_seats()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 12, ticket: Ticket("Team of 5", 6000m, 10, groupSize: 5));
        var anon = factory.NewClient();
        var buyer = HtnApiFactory.UniqueEmail("buyer");
        var created = await RegisterOkAsync(anon, ev.Slug, Body(ev.TicketId,
            [Person(buyer), Member(), Member(), Member(), Member(buyer, "Same", "Address")]));
        var ids = created.GetProperty("participantIds").Strings();
        Assert.Equal(5, ids.Length);
        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(ParticipantIdPattern(), id));
        Assert.Equal(6000m, created.GetProperty("amountDue").GetDecimal());

        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"));
        Assert.Equal(5, detail.GetProperty("sold").GetInt32());                         // 5 seats of the capacity
        Assert.Equal(1, detail.GetProperty("tickets")[0].GetProperty("sold").GetInt32()); // 1 of the ticket quantity

        // 12 seats: a second group fits (10), a third doesn't.
        await RegisterOkAsync(anon, ev.Slug, Body(ev.TicketId, [Person(), Member(), Member(), Member(), Member()]));
        var third = await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(), Member(), Member(), Member(), Member()]));
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("sold_out", (await ApiClient.JsonAsync(third)).Code());
    }

    [Fact]
    public async Task Capacity_and_ticket_quantity_are_enforced()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var anon = factory.NewClient();

        var byCapacity = await PublishedAsync(board, capacity: 2);
        await RegisterOkAsync(anon, byCapacity.Slug, Body(byCapacity.TicketId, [Person()]));
        await RegisterOkAsync(anon, byCapacity.Slug, Body(byCapacity.TicketId, [Person()]));
        var full = await RegisterAsync(anon, byCapacity.Slug, Body(byCapacity.TicketId, [Person()]));
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("sold_out", (await ApiClient.JsonAsync(full)).Code());
        var page = await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/events/{byCapacity.Slug}"));
        Assert.Equal(0, page.GetProperty("tickets")[0].GetProperty("remaining").GetInt32());

        var byQuantity = await PublishedAsync(board, ticket: Ticket("Few", 500m, 1));
        await RegisterOkAsync(anon, byQuantity.Slug, Body(byQuantity.TicketId, [Person()]));
        var gone = await RegisterAsync(anon, byQuantity.Slug, Body(byQuantity.TicketId, [Person()]));
        Assert.Equal("sold_out", (await ApiClient.JsonAsync(gone)).Code());
    }

    [Fact]
    public async Task Concurrent_checkouts_never_oversell()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 400, ticket: Ticket("Few", 500m, 3));
        var clients = Enumerable.Range(0, 12).Select(_ => factory.NewClient()).ToList();
        // Fetch CSRF tokens first so the requests themselves start together.
        var bodies = clients.Select(_ => Body(ev.TicketId, [Person()])).ToList();
        var results = await Task.WhenAll(clients.Select((c, i) => RegisterAsync(c, ev.Slug, bodies[i])));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, await factory.DbAsync(db => db.Registrations.CountAsync(r => r.EventId == ev.Id)));
    }

    [Fact]
    public async Task Concurrent_checkouts_respect_the_voucher_limit()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        await AddVoucherAsync(board, ev.Id, Voucher("ONCE-ONLY", usageLimit: 2));
        var clients = Enumerable.Range(0, 8).Select(_ => factory.NewClient()).ToList();
        var results = await Task.WhenAll(clients.Select(c => RegisterAsync(c, ev.Slug, Body(ev.TicketId, [Person()], "once-only"))));
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(6, results.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Vouchers_check_limit_expiry_and_scope_and_never_say_why()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var student = (await AddTicketAsync(board, ev.Id, Ticket("Student", 800m, 40))).Id();
        await AddVoucherAsync(board, ev.Id, Voucher("STUDENT20", ticketTypeIds: [student], usageLimit: 1));
        await AddVoucherAsync(board, ev.Id, Voucher("EXPIRED", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var inactive = await AddVoucherAsync(board, ev.Id, Voucher("RETIRED"));
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/vouchers/{inactive}/deactivate")).StatusCode);
        var anon = factory.NewClient();

        async Task<JsonElement> Check(string code, Guid ticket) =>
            await ApiClient.JsonAsync(await anon.PostAsync($"/api/public/events/{ev.Slug}/voucher-check", new { code, ticketTypeId = ticket }));

        var ok = await Check("student20", student);
        Assert.True(ok.GetProperty("valid").GetBoolean());
        Assert.Equal(160m, ok.GetProperty("discount").GetDecimal());
        Assert.Equal(640m, ok.GetProperty("total").GetDecimal());

        // Unknown, expired, inactive and out-of-scope all look the same: not valid, no discount, the full price.
        var sameTicket = new[] { await Check("NOPE", student), await Check("EXPIRED", student), await Check("RETIRED", student) };
        static string Shape(JsonElement r) =>
            $"{r.GetProperty("valid").GetBoolean()}|{r.GetProperty("discount").GetDouble()}|{r.GetProperty("total").GetDouble()}|{r.EnumerateObject().Count()}";
        Assert.All(sameTicket, r => Assert.Equal("False|0|800|3", Shape(r)));
        Assert.Equal("False|0|1500|3", Shape(await Check("STUDENT20", ev.TicketId)));

        foreach (var code in new[] { "EXPIRED", "RETIRED", "NOPE" })
        {
            var res = await RegisterAsync(anon, ev.Slug, Body(student, [Person()], code));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("invalid_voucher", (await ApiClient.JsonAsync(res)).Code());
        }
        Assert.Equal("invalid_voucher", (await ApiClient.JsonAsync(await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()], "STUDENT20")))).Code());

        // Limit 1: the first use works, then it's used up (the check says invalid too).
        var used = await RegisterOkAsync(anon, ev.Slug, Body(student, [Person()], "STUDENT20"));
        Assert.Equal(640m, used.GetProperty("amountDue").GetDecimal());
        Assert.Equal("invalid_voucher", (await ApiClient.JsonAsync(await RegisterAsync(anon, ev.Slug, Body(student, [Person()], "STUDENT20")))).Code());
        Assert.False((await Check("STUDENT20", student)).GetProperty("valid").GetBoolean());
        var voucher = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"))).GetProperty("vouchers")
            .EnumerateArray().Single(v => v.GetProperty("code").GetString() == "STUDENT20");
        Assert.Equal(1, voucher.GetProperty("used").GetInt32());

        // Cancelling frees the use.
        var regId = await RegistrationIdAsync(factory, used.GetProperty("participantIds")[0].GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{regId}/cancel", new { reason = "Duplicate" })).StatusCode);
        Assert.True((await Check("STUDENT20", student)).GetProperty("valid").GetBoolean());
        await RegisterOkAsync(anon, ev.Slug, Body(student, [Person()], "STUDENT20"));
    }

    [Fact]
    public async Task Zero_total_goes_straight_to_paid_and_queues_the_ticket()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        await AddVoucherAsync(board, ev.Id, Voucher("FREE100", value: 100m));
        var email = HtnApiFactory.UniqueEmail("free");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)], "FREE100"));
        Assert.Equal("Paid", created.GetProperty("status").GetString());
        Assert.Equal(0m, created.GetProperty("amountDue").GetDecimal());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("deadline").ValueKind);

        var regId = await RegistrationIdAsync(factory, created.GetProperty("participantIds")[0].GetString()!);
        var payment = await factory.DbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.RegistrationId == regId));
        Assert.Equal(PaymentMethod.Comp, payment.Method);
        Assert.Equal(0m, payment.Amount);
        Assert.DoesNotContain(await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == RegistrationEmailTypes.Billing).ToListAsync()),
            o => o.PayloadJson.Contains(regId.ToString(), StringComparison.Ordinal));

        await factory.DrainOutboxAsync();
        var ticket = factory.Emails.To(email).Single();
        Assert.Equal(EmailCategories.Ticket, ticket.Category);
        Assert.Contains(ticket.AllAttachments, a => a.ContentId == RegistrationEmails.QrContentId && a.ContentType == "image/png");
    }
}
