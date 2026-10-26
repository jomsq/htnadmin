using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.CheckIn.CheckInTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

/// <summary>Phase 3 review fixes: database guards, overdue seat release, comp reasons, input rules, proof uploads, resends.</summary>
public sealed class IntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    private async Task<(Guid RegId, string Pid, string Email)> RegisterPublicAsync(OnSaleEvent ev, string? email = null)
    {
        email ??= HtnApiFactory.UniqueEmail("guard");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        var pid = created.GetProperty("participantIds")[0].GetString()!;
        return (await RegistrationIdAsync(factory, pid), pid, email);
    }

    private static Task<HttpResponseMessage> MarkPaidAsync(ApiClient board, Guid eventId, object body) =>
        board.PostAsync($"/api/events/{eventId}/registrations/mark-paid", body);

    // ---------- Database guards (raw SQL, as if the API were bypassed) ----------

    [Fact]
    public async Task Registrations_reject_delete_frozen_columns_and_bad_status_moves()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var (id, _, _) = await RegisterPublicAsync(ev);

        await AssertRejectedAsync($"DELETE FROM registrations WHERE id = {id}", "never deleted");
        await AssertRejectedAsync($"UPDATE registrations SET total = 1, subtotal = 1 WHERE id = {id}", "immutable");
        await AssertRejectedAsync($"UPDATE registrations SET created_at = created_at - interval '1 day' WHERE id = {id}", "immutable");
        await AssertRejectedAsync($"UPDATE registrations SET source = 'Manual', created_by_id = gen_random_uuid() WHERE id = {id}", "immutable");
        // PendingPayment -> CheckedIn is never allowed (only Paid -> CheckedIn).
        await AssertRejectedAsync($"UPDATE registrations SET status = 'CheckedIn', paid_at = now(), checked_in_at = now() WHERE id = {id}", "not allowed");
        // Deadline before creation.
        await AssertRejectedAsync($"UPDATE registrations SET deadline = created_at - interval '1 hour' WHERE id = {id}", "ck_registrations_deadline");
        // Paid without paid_at.
        await AssertRejectedAsync($"UPDATE registrations SET status = 'Paid' WHERE id = {id}", "ck_registrations_paid");
        // Updating mutable columns is fine.
        Assert.Equal(1, await SqlAsync($"UPDATE registrations SET updated_at = now() WHERE id = {id}"));

        // CheckedIn is final.
        Assert.Equal(HttpStatusCode.OK, (await MarkPaidAsync(board, ev.Id, new { ids = new[] { id }, method = "Cash" })).StatusCode);
        var holder = (await HoldersAsync(factory, id))[0];
        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, holder.Token)).Result());
        await AssertRejectedAsync($"UPDATE registrations SET status = 'Paid', checked_in_at = NULL WHERE id = {id}", "not allowed");
    }

    [Fact]
    public async Task Attendees_reject_delete_and_identity_changes_and_must_match_their_registrations_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var other = await PublishedAsync(board);
        var (id, pid, _) = await RegisterPublicAsync(ev);
        var attendee = await factory.DbAsync(db => db.Attendees.AsNoTracking().SingleAsync(a => a.RegistrationId == id));

        await AssertRejectedAsync($"DELETE FROM attendees WHERE id = {attendee.Id}", "never deleted");
        await AssertRejectedAsync($"UPDATE attendees SET participant_id = 'HTN-2222-2222' WHERE id = {attendee.Id}", "immutable");
        await AssertRejectedAsync($"UPDATE attendees SET event_id = {other.Id} WHERE id = {attendee.Id}", "immutable");
        Assert.Equal(1, await SqlAsync($"UPDATE attendees SET shirt_size = 'L' WHERE id = {attendee.Id}"));

        // Composite foreign key: an attendee row can't point at a registration of another event.
        await AssertRejectedAsync($"""
            INSERT INTO attendees (id, registration_id, event_id, sort_order, participant_id, first_name, last_name, email, ticket_version)
            VALUES (gen_random_uuid(), {id}, {other.Id}, 1, 'HTN-3333-3333', 'Eve', 'Cruz', 'eve@example.ph', 1)
            """, "fk_attendees_registrations_registration_id_event_id");
        // ... nor can a registration use another event's ticket type.
        await AssertRejectedAsync($"""
            INSERT INTO registrations (id, event_id, ticket_type_id, seats, subtotal, discount, total, status, deadline, source,
                created_at, consent_at, consent_version)
            VALUES (gen_random_uuid(), {other.Id}, {ev.TicketId}, 1, 0, 0, 0, 'PendingPayment', NULL, 'Public', now(), now(), 'v1')
            """, "fk_registrations_ticket_types_ticket_type_id_event_id");
        Assert.Equal(pid, attendee.ParticipantId);
    }

    [Fact]
    public async Task Payments_allow_insert_for_the_total_and_a_single_revert_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var (id, _, _) = await RegisterPublicAsync(ev);

        // Wrong amount / pre-reverted / non-comp zero inserts are rejected.
        await AssertRejectedAsync($"""
            INSERT INTO payments (id, registration_id, method, amount, received_at, marked_at)
            VALUES (gen_random_uuid(), {id}, 'Cash', 10, now(), now())
            """, "must equal the registration total");
        await AssertRejectedAsync($"""
            INSERT INTO payments (id, registration_id, method, amount, received_at, marked_at, reverted_at, revert_reason)
            VALUES (gen_random_uuid(), {id}, 'Cash', 1500, now(), now(), now(), 'x')
            """, "reverted already");
        await AssertRejectedAsync($"""
            INSERT INTO payments (id, registration_id, method, amount, received_at, marked_at)
            VALUES (gen_random_uuid(), {id}, 'Comp', 5, now(), now())
            """, "ck_payments_comp");

        Assert.Equal(HttpStatusCode.OK, (await MarkPaidAsync(board, ev.Id, new { ids = new[] { id }, method = "Cash" })).StatusCode);
        var paymentId = await factory.DbAsync(db => db.Payments.Where(p => p.RegistrationId == id).Select(p => p.Id).SingleAsync());
        await AssertRejectedAsync($"UPDATE payments SET amount = 1 WHERE id = {paymentId}", "single revert");
        await AssertRejectedAsync($"UPDATE payments SET reference = 'edited' WHERE id = {paymentId}", "single revert");
        await AssertRejectedAsync($"DELETE FROM payments WHERE id = {paymentId}", "never deleted");
        await AssertRejectedAsync($"TRUNCATE payments CASCADE", "append-only");

        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{ev.Id}/registrations/{id}/revert-payment", new { reason = "Wrong row" })).StatusCode);
        await AssertRejectedAsync($"UPDATE payments SET revert_reason = 'changed' WHERE id = {paymentId}", "single revert");
    }

    [Fact]
    public async Task Voucher_scope_and_event_account_arrays_reject_nulls_and_oversize()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var voucherId = await AddVoucherAsync(board, ev.Id, Voucher("ARRAYS1"));
        await AssertRejectedAsync($"UPDATE vouchers SET ticket_type_ids = ARRAY[NULL]::uuid[] WHERE id = {voucherId}", "ck_vouchers_ticket_type_ids");
        await AssertRejectedAsync(
            $"UPDATE vouchers SET ticket_type_ids = ARRAY(SELECT gen_random_uuid() FROM generate_series(1, 51)) WHERE id = {voucherId}",
            "ck_vouchers_ticket_type_ids");
        await AssertRejectedAsync(
            $"UPDATE events SET email_payment_account_ids = ARRAY(SELECT gen_random_uuid() FROM generate_series(1, 11)) WHERE id = {ev.Id}",
            "ck_events_email_payment_accounts");
        // Voucher codes stay unique per event (now a plain unique index on the upper-case code).
        var dup = await board.PostAsync($"/api/events/{ev.Id}/vouchers", Voucher("arrays1"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    // ---------- Overdue registrations release their seat ----------

    [Fact]
    public async Task Overdue_registration_releases_its_seat_and_paying_it_rechecks_capacity()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 1);
        var late = await RegisterPublicAsync(ev);
        Assert.Equal("sold_out", (await ApiClient.JsonAsync(await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person()])))).Code());

        await factory.MakeOverdueAsync(late.RegId);
        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"));
        Assert.Equal(0, detail.GetProperty("sold").GetInt32());

        // The released seat goes to someone else; the overdue record is kept, still pending and flagged overdue.
        var taker = await RegisterPublicAsync(ev);
        var list = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations?overdue=true"));
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(late.RegId, row.GetProperty("id").GetGuid());
        Assert.Equal("PendingPayment", row.GetProperty("status").GetString());

        // Paying the overdue one now doesn't fit: 409, nothing changes, unless an override reason is given.
        var full = await MarkPaidAsync(board, ev.Id, new { ids = new[] { late.RegId }, method = "Cash" });
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("capacity_exceeded", (await ApiClient.JsonAsync(full)).Code());
        Assert.Equal(RegistrationStatus.PendingPayment, await factory.DbAsync(db => db.Registrations.Where(r => r.Id == late.RegId).Select(r => r.Status).SingleAsync()));
        Assert.Equal(0, await factory.DbAsync(db => db.Payments.CountAsync(p => p.RegistrationId == late.RegId)));

        var tooLong = await MarkPaidAsync(board, ev.Id, new { ids = new[] { late.RegId }, method = "Cash", overrideCapacityReason = new string('x', 501) });
        Assert.True((await ApiClient.JsonAsync(tooLong)).HasError("overrideCapacityReason"));

        var ok = await MarkPaidAsync(board, ev.Id, new { ids = new[] { late.RegId }, method = "Cash", overrideCapacityReason = "Paid at the desk, extra chair" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "registration.mark_paid" && a.EntityId == late.RegId.ToString()));
        Assert.Contains("Paid at the desk, extra chair", audit.AfterJson!, StringComparison.Ordinal);
        Assert.Equal(2, (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"))).GetProperty("sold").GetInt32());
        Assert.NotEqual(taker.RegId, late.RegId);
    }

    [Fact]
    public async Task Overdue_registration_that_still_fits_is_paid_without_an_override()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 5);
        var late = await RegisterPublicAsync(ev);
        await factory.MakeOverdueAsync(late.RegId);
        Assert.Equal(HttpStatusCode.OK, (await MarkPaidAsync(board, ev.Id, new { ids = new[] { late.RegId }, method = "Cash" })).StatusCode);
    }

    [Fact]
    public async Task Overdue_payment_and_a_new_checkout_race_for_the_last_seat_and_only_one_wins()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 1);
        var late = await RegisterPublicAsync(ev);
        await factory.MakeOverdueAsync(late.RegId);

        var pay = MarkPaidAsync(board, ev.Id, new { ids = new[] { late.RegId }, method = "Cash" });
        var buy = RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person()]));
        await Task.WhenAll(pay, buy);
        var paid = pay.Result.StatusCode == HttpStatusCode.OK;
        var bought = buy.Result.StatusCode == HttpStatusCode.Created;
        Assert.True(paid ^ bought, $"pay {(int)pay.Result.StatusCode}, buy {(int)buy.Result.StatusCode}");
        Assert.Equal(1, (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}"))).GetProperty("sold").GetInt32());
    }

    // ---------- Comp reason, manual registration overrides ----------

    [Fact]
    public async Task Comp_on_money_due_needs_a_reason_which_is_audited()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var (id, _, _) = await RegisterPublicAsync(ev);

        var noReason = await MarkPaidAsync(board, ev.Id, new { ids = new[] { id }, method = "Comp" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var problem = await ApiClient.JsonAsync(noReason);
        Assert.True(problem.HasError("reason"));
        Assert.Equal("reason_required", problem.Code());

        var ok = await MarkPaidAsync(board, ev.Id, new { ids = new[] { id }, method = "Comp", reason = "Speaker's guest" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "registration.mark_paid" && a.EntityId == id.ToString()));
        Assert.Equal("Speaker's guest", audit.Reason);
        Assert.Equal(0m, await factory.DbAsync(db => db.Payments.Where(p => p.RegistrationId == id).Select(p => p.Amount).SingleAsync()));
    }

    [Fact]
    public async Task Manual_registration_mark_paid_takes_comp_reason_and_capacity_override()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, capacity: 1);
        var url = $"/api/events/{ev.Id}/registrations";

        var comp = await board.PostAsync(url, new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true, markPaid = new { method = "Comp" } });
        Assert.Equal(HttpStatusCode.BadRequest, comp.StatusCode);
        Assert.True((await ApiClient.JsonAsync(comp)).HasError("markPaid.reason"));

        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync(url, new
        {
            ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true, markPaid = new { method = "Comp", reason = "Sponsor guest" },
        })).StatusCode);

        // Full: unpaid manual is sold out; marked paid at once needs an override.
        Assert.Equal("sold_out", (await ApiClient.JsonAsync(await board.PostAsync(url, new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true }))).Code());
        var full = await board.PostAsync(url, new { ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true, markPaid = new { method = "Cash" } });
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("capacity_exceeded", (await ApiClient.JsonAsync(full)).Code());
        var over = await board.PostAsync(url, new
        {
            ticketTypeId = ev.TicketId, attendees = new[] { Person() }, consent = true,
            markPaid = new { method = "Cash", overrideCapacityReason = "Walk-in, paid cash" },
        });
        Assert.Equal(HttpStatusCode.Created, over.StatusCode);
        var regId = (await ApiClient.JsonAsync(over)).Id();
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "registration.mark_paid" && a.EntityId == regId.ToString()));
        Assert.Contains("Walk-in, paid cash", audit.AfterJson!, StringComparison.Ordinal);
    }

    // ---------- Form input rules ----------

    [Theory]
    [InlineData("Ma. Niña", "Dela Cruz-Ñañez", null)]
    [InlineData("José", "O'Brien", "R&D (IT/Security), Region 1")]
    [InlineData("Ma. Niña", "Dela Cruz", "DICT")] // n + combining tilde
    public async Task Filipino_and_accented_names_are_accepted(string first, string last, string? company)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var res = await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(first: first, last: last, company: company)]));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Theory]
    [InlineData("Juan https://x", "Cruz", null, "attendees[0].firstName")]
    [InlineData("Juan", "<b>Cruz</b>", null, "attendees[0].lastName")]
    [InlineData("juan@example.ph", "Cruz", null, "attendees[0].firstName")]
    [InlineData("Juan2", "Cruz", null, "attendees[0].firstName")]
    [InlineData("Juan", "Cruz", "Visit https://spam.example", "attendees[0].company")]
    [InlineData("Juan", "Cruz", "<script>", "attendees[0].company")]
    public async Task Links_markup_and_digits_in_names_are_rejected(string first, string last, string? company, string field)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var res = await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(first: first, last: last, company: company)]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).HasError(field));
    }

    [Fact]
    public async Task Public_group_needs_distinct_emails_and_public_tickets_cap_groups_at_20()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, ticket: Ticket("Pair", 2000m, 10, groupSize: 2));
        var email = HtnApiFactory.UniqueEmail("dup");
        var res = await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email), Member(email.ToUpperInvariant())]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).HasError("attendees[1].email"));

        var big = await board.PostAsync($"/api/events/{ev.Id}/tickets", Ticket("Big public", 100m, 10, groupSize: 21));
        Assert.True((await ApiClient.JsonAsync(big)).HasError("groupSize"));
        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync($"/api/events/{ev.Id}/tickets", Ticket("Big hidden", 100m, 10, groupSize: 21, visibility: "Hidden"))).StatusCode);
        await AssertRejectedAsync($"UPDATE ticket_types SET visibility = 'Public' WHERE event_id = {ev.Id} AND group_size = 21", "ck_ticket_types_group_size");
    }

    // ---------- Proof uploads ----------

    private static async Task<HttpResponseMessage> UploadAsync(ApiClient client, string token, byte[] file)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/proof/{token}");
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "screenshot.png");
        req.Content = form;
        req.Headers.Add("X-CSRF-TOKEN", await client.CsrfTokenAsync());
        return await client.Http.SendAsync(req);
    }

    private async Task<(OnSaleEvent Ev, Guid RegId, string Token)> ProofRegistrationAsync()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var (id, _, email) = await RegisterPublicAsync(ev);
        await factory.DrainOutboxAsync();
        return (ev, id, factory.Emails.ProofToken(email));
    }

    [Fact]
    public async Task Parallel_proof_uploads_stop_at_the_cap()
    {
        var (_, regId, token) = await ProofRegistrationAsync();
        var clients = Enumerable.Range(0, 6).Select(_ => factory.NewClient()).ToList();
        var results = await Task.WhenAll(clients.Select(c => UploadAsync(c, token, Png)));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, await factory.DbAsync(db => db.PaymentProofs.CountAsync(p => p.RegistrationId == regId)));
    }

    [Fact]
    public async Task Png_text_chunks_are_stripped_before_storing()
    {
        var (ev, regId, token) = await ProofRegistrationAsync();
        var png = PngWithText("Comment", "GPS 18.1977 N 120.5936 E, Juan's phone");
        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(factory.NewClient(), token, png)).StatusCode);

        var board = await factory.SignedInAsync(PersonLevel.Board);
        var stored = await (await board.GetAsync($"/api/events/{ev.Id}/registrations/{regId}/proof")).Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("GPS 18.1977", Encoding.Latin1.GetString(stored), StringComparison.Ordinal);
        Assert.Equal(Png, stored);
    }

    /// <summary>The 1×1 test PNG with a tEXt chunk inserted after IHDR (CRC is not checked by the stripper or by us).</summary>
    internal static byte[] PngWithText(string key, string value)
    {
        var data = Encoding.Latin1.GetBytes(key + "\0" + value);
        var chunk = new byte[12 + data.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk, 8);
        const int afterIhdr = 8 + 25;
        return [.. Png[..afterIhdr], .. chunk, .. Png[afterIhdr..]];
    }

    // ---------- Lists, resends ----------

    [Fact]
    public async Task List_keeps_payment_reference_and_registrant_reference_apart_and_search_stays_in_the_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var other = await PublishedAsync(board);
        var tag = LetterTag("Srch");
        await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(first: "Ana", last: tag)]));
        await RegisterOkAsync(factory.NewClient(), other.Slug, Body(other.TicketId, [Person(first: "Ana", last: tag)]));
        var list = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations?search={tag}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var row = list.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reference").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("proofReference").ValueKind);
    }

    [Fact]
    public async Task Resend_ticket_for_a_group_partly_in_goes_only_to_members_not_checked_in()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, ticket: Ticket("Trio", 3000m, 10, groupSize: 3));
        var team = await RegisterAsync(factory, board, ev.Id, ev.TicketId, people: [Person(), Member(), Member()]);
        await ScanOkAsync(board, ev.Id, team[1].Token);
        await factory.DrainOutboxAsync();
        int Tickets(string email) => factory.Emails.To(email).Count(m => m.Category == Htn.Infrastructure.Email.EmailCategories.Ticket);
        var before = team.Select(t => Tickets(t.Email)).ToList();

        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/events/{ev.Id}/registrations/{team[0].RegistrationId}/resend-ticket")).StatusCode);
        await factory.DrainOutboxAsync();
        Assert.Equal(before[1], Tickets(team[1].Email));      // already in: nothing resent
        Assert.Equal(before[2] + 1, Tickets(team[2].Email));  // not in yet: own ticket again
        Assert.Equal(before[0] + 2, Tickets(team[0].Email));  // buyer: own ticket + copy of member 3's

        await ScanOkAsync(board, ev.Id, team[0].Token);
        await ScanOkAsync(board, ev.Id, team[2].Token);
        var none = await board.PostAsync($"/api/events/{ev.Id}/registrations/{team[0].RegistrationId}/resend-ticket");
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.Equal("all_checked_in", (await ApiClient.JsonAsync(none)).Code());
    }
}
