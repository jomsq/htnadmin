using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Events;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.CheckIn;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Htn.Api.Tests.CheckIn.CheckInTestData;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.CheckIn;

public sealed class CheckInEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static (HttpMethod Method, string Url, object? Body)[] Routes(Guid eventId) =>
    [
        (HttpMethod.Post, ScanUrl(eventId), Scan("HTN-2345-6789")),
        (HttpMethod.Post, $"/api/events/{eventId}/check-in/manual", new { attendeeId = Guid.NewGuid(), station = "Desk 1" }),
        (HttpMethod.Get, $"/api/events/{eventId}/check-in/stats?station=Desk%201", null),
        (HttpMethod.Get, $"/api/events/{eventId}/check-in/recent", null),
        (HttpMethod.Get, $"/api/events/{eventId}/check-in/search?q=maria", null),
    ];

    private Task<List<CheckInScan>> ScansAsync(Guid eventId) =>
        factory.DbAsync(db => db.CheckInScans.AsNoTracking().Where(s => s.EventId == eventId).OrderBy(s => s.At).ThenBy(s => s.Id).ToListAsync());

    // ---------- Authorization and event state ----------

    [Fact]
    public async Task Anonymous_is_401_on_every_route()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var anon = factory.NewClient();
        foreach (var (method, url, body) in Routes(ev.Id))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(method, url, body, csrf: method != HttpMethod.Get)).StatusCode);
        Assert.Empty(await ScansAsync(ev.Id));
    }

    [Theory]
    [InlineData(PersonLevel.Board)]
    [InlineData(PersonLevel.Goon)]
    [InlineData(PersonLevel.Apprentice)]
    public async Task Every_staff_role_can_use_the_desk(PersonLevel level)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var staff = await factory.SignedInAsync(level);

        Assert.Equal("Welcome", (await ScanOkAsync(staff, ev.Id, guest.Token)).Result());
        foreach (var (method, url, body) in Routes(ev.Id))
            Assert.Equal(HttpStatusCode.OK, (await staff.SendAsync(method, url, body, csrf: method != HttpMethod.Get)).StatusCode);
    }

    [Fact]
    public async Task Draft_and_unknown_events_are_404()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var draft = (await CreateAsync(board)).Id();
        foreach (var eventId in new[] { draft, Guid.NewGuid() })
            foreach (var (method, url, body) in Routes(eventId))
                Assert.Equal(HttpStatusCode.NotFound, (await board.SendAsync(method, url, body, csrf: method != HttpMethod.Get)).StatusCode);
        Assert.Empty(await ScansAsync(draft));
    }

    [Fact]
    public async Task Check_in_is_closed_more_than_a_day_before_or_after_the_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var early = await PublishedAsync(board, startsAt: DateTimeOffset.UtcNow.AddDays(2));
        var guest = await PaidAsync(factory, board, early);
        async Task AssertClosedAsync(Guid eventId)
        {
            foreach (var (method, url, body) in Routes(eventId))
            {
                var res = await board.SendAsync(method, url, method == HttpMethod.Post && url.EndsWith("/scan", StringComparison.Ordinal)
                    ? Scan(guest.Token) : body, csrf: method != HttpMethod.Get);
                Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
                Assert.Equal("check_in_closed", (await ApiClient.JsonAsync(res)).Code());
            }
        }
        await AssertClosedAsync(early.Id);
        Assert.Empty(await ScansAsync(early.Id));
        Assert.Null(await factory.DbAsync(db => db.Attendees.Where(a => a.Id == guest.AttendeeId).Select(a => a.CheckedInAt).SingleAsync()));

        // 23 hours before the start it opens; more than a day after the end it closes again.
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE events SET starts_at = now() + interval '23 hours', ends_at = now() + interval '30 hours' WHERE id = {early.Id}"));
        Assert.Equal("Welcome", (await ScanOkAsync(board, early.Id, guest.Token)).Result());
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE events SET starts_at = now() - interval '3 days', ends_at = now() - interval '25 hours' WHERE id = {early.Id}"));
        await AssertClosedAsync(early.Id);
    }

    [Fact]
    public async Task Closed_event_still_checks_in()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{ev.Id}/close")).StatusCode);
        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, guest.Token)).Result());
    }

    [Fact]
    public async Task Invalid_requests_are_400()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var noStation = await ApiClient.JsonAsync(await board.PostAsync(ScanUrl(ev.Id), new { payload = "x", station = "   " }));
        Assert.True(noStation.HasError("station"));
        var longStation = await board.PostAsync(ScanUrl(ev.Id), Scan("x", new string('a', 41)));
        Assert.Equal(HttpStatusCode.BadRequest, longStation.StatusCode);
        var noPayload = await ApiClient.JsonAsync(await board.PostAsync(ScanUrl(ev.Id), new { payload = "", station = "Desk 1" }));
        Assert.True(noPayload.HasError("payload"));
        var manual = await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/check-in/manual", new { station = "Desk 1" }));
        Assert.True(manual.HasError("attendeeId"));
        Assert.Equal(HttpStatusCode.BadRequest, (await board.GetAsync($"/api/events/{ev.Id}/check-in/recent?station={new string('a', 41)}")).StatusCode);
        Assert.Empty(await ScansAsync(ev.Id));
    }

    // ---------- Results ----------

    [Fact]
    public async Task Welcome_checks_in_the_attendee_and_registration_with_audit_and_log()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev, first: "Maria", last: "Santos");
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var staff = factory.NewClient();
        await staff.LoginOkAsync(goon.Email, HtnApiFactory.Password);

        var scan = await ScanOkAsync(staff, ev.Id, guest.Token, "  Main entrance · Desk 2  ");
        Assert.Equal("Welcome", scan.Result());
        var card = scan.GetProperty("attendee");
        Assert.Equal("Maria Santos", card.GetProperty("name").GetString());
        Assert.Equal("Regular", card.GetProperty("ticketName").GetString());
        Assert.Equal(guest.ParticipantId, card.GetProperty("participantId").GetString());
        Assert.Equal("M", card.GetProperty("shirtSize").GetString());
        Assert.False(card.GetProperty("isMember").GetBoolean());
        Assert.False(card.TryGetProperty("email", out _));
        Assert.Equal("Main entrance · Desk 2", scan.GetProperty("checkedInStation").GetString());
        Assert.NotEqual(JsonValueKind.Null, scan.GetProperty("checkedInAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, scan.GetProperty("amountDue").ValueKind);

        var attendee = await factory.DbAsync(db => db.Attendees.AsNoTracking().SingleAsync(a => a.Id == guest.AttendeeId));
        Assert.NotNull(attendee.CheckedInAt);
        Assert.Equal("Main entrance · Desk 2", attendee.CheckedInStation);
        Assert.Equal(goon.UserId, attendee.CheckedInById);
        var reg = await factory.DbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == guest.RegistrationId));
        Assert.Equal(RegistrationStatus.CheckedIn, reg.Status);
        Assert.Equal(attendee.CheckedInAt, reg.CheckedInAt);

        var log = Assert.Single(await ScansAsync(ev.Id));
        Assert.Equal(CheckInResult.Welcome, log.Result);
        Assert.Equal(CheckInMethod.Qr, log.Method);
        Assert.Equal(guest.AttendeeId, log.AttendeeId);
        Assert.Equal(goon.UserId, log.StaffUserId);
        Assert.Equal("Main entrance · Desk 2", log.Station);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "attendee.check_in" && a.EntityId == guest.AttendeeId.ToString()));
        Assert.Equal(goon.UserId, audit.ActorId);
        Assert.Contains("CheckedIn", audit.AfterJson!, StringComparison.Ordinal);
        Assert.Contains("Paid", audit.BeforeJson!, StringComparison.Ordinal);

        // A checked-in registration can no longer be cancelled or reverted.
        Assert.Equal(HttpStatusCode.Conflict,
            (await board.PostAsync($"/api/events/{ev.Id}/registrations/{guest.RegistrationId}/cancel", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task Second_scan_is_already_checked_in_with_first_time_and_station()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var first = await ScanOkAsync(board, ev.Id, guest.Token, "Desk 1");
        var again = await ScanOkAsync(board, ev.Id, guest.Token, "Desk 2");

        Assert.Equal("AlreadyCheckedIn", again.Result());
        Assert.Equal("Desk 1", again.GetProperty("checkedInStation").GetString());
        Assert.Equal(first.GetProperty("checkedInAt").GetDateTimeOffset(), again.GetProperty("checkedInAt").GetDateTimeOffset());
        Assert.Equal(guest.ParticipantId, again.GetProperty("attendee").GetProperty("participantId").GetString());

        var logs = await ScansAsync(ev.Id);
        Assert.Equal([CheckInResult.Welcome, CheckInResult.AlreadyCheckedIn], logs.Select(l => l.Result));
        Assert.Equal(1, await factory.DbAsync(db => db.AuditEntries.CountAsync(a => a.Action == "attendee.check_in" && a.EntityId == guest.AttendeeId.ToString())));
        Assert.Equal("Desk 1", await factory.DbAsync(db => db.Attendees.Where(a => a.Id == guest.AttendeeId).Select(a => a.CheckedInStation).SingleAsync()));
    }

    [Fact]
    public async Task Unpaid_ticket_is_not_paid_with_the_amount_due()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = (await RegisterAsync(factory, board, ev.Id, ev.TicketId, paid: false))[0];

        var scan = await ScanOkAsync(board, ev.Id, guest.Token);
        Assert.Equal("NotPaid", scan.Result());
        Assert.Equal(1500m, scan.GetProperty("amountDue").GetDecimal());
        Assert.Equal(JsonValueKind.Null, scan.GetProperty("checkedInAt").ValueKind);
        Assert.Equal(guest.ParticipantId, scan.GetProperty("attendee").GetProperty("participantId").GetString());

        // A typed ID only looks up: the card says not paid, nothing changes.
        var byId = await ScanOkAsync(board, ev.Id, guest.ParticipantId);
        Assert.Equal("Lookup", byId.Result());
        Assert.Equal("PendingPayment", byId.GetProperty("status").GetString());
        Assert.Equal(1500m, byId.GetProperty("amountDue").GetDecimal());
        Assert.Equal(guest.AttendeeId, byId.GetProperty("attendeeId").GetGuid());
        Assert.Null(await factory.DbAsync(db => db.Attendees.Where(a => a.Id == guest.AttendeeId).Select(a => a.CheckedInAt).SingleAsync()));
        Assert.Equal(RegistrationStatus.PendingPayment,
            await factory.DbAsync(db => db.Registrations.Where(r => r.Id == guest.RegistrationId).Select(r => r.Status).SingleAsync()));
    }

    [Fact]
    public async Task Cancelled_registration_is_cancelled_by_qr_and_by_participant_id()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{ev.Id}/registrations/{guest.RegistrationId}/cancel", new { reason = "Asked for a refund" })).StatusCode);

        var scan = await ScanOkAsync(board, ev.Id, guest.Token);
        Assert.Equal("Cancelled", scan.Result());
        Assert.Equal(guest.ParticipantId, scan.GetProperty("attendee").GetProperty("participantId").GetString());
        var lookup = await ScanOkAsync(board, ev.Id, guest.ParticipantId);
        Assert.Equal("Lookup", lookup.Result());
        Assert.Equal("Cancelled", lookup.GetProperty("status").GetString());
        Assert.Equal([CheckInResult.Cancelled, CheckInResult.Lookup], (await ScansAsync(ev.Id)).Select(x => x.Result));
        // Manual check-in of a cancelled attendee is refused the same way as the QR.
        Assert.Equal("Cancelled", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/check-in/manual",
            new { attendeeId = guest.AttendeeId, station = "Desk 1" }))).Result());
    }

    [Fact]
    public async Task Tampered_other_event_stale_and_unknown_codes_are_invalid_and_logged()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var other = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var outsider = await PaidAsync(factory, board, other);
        var tokens = factory.Services.GetRequiredService<ITicketToken>();

        // Flip one character of the signature.
        var dot = guest.Token.IndexOf('.');
        var sig = guest.Token[(dot + 1)..].ToCharArray();
        sig[3] = sig[3] == 'A' ? 'B' : 'A';
        var tampered = guest.Token[..(dot + 1)] + new string(sig);
        // A genuine token re-signed for this event but naming another event's attendee.
        var crossed = tokens.Issue(ev.Id, outsider.AttendeeId, 1);

        foreach (var payload in new[] { tampered, outsider.Token, crossed, "not a ticket", "HTN-ZZZZ-ZZZZ", outsider.ParticipantId, guest.Token + "x" })
        {
            var scan = await ScanOkAsync(board, ev.Id, payload);
            Assert.Equal("Invalid", scan.Result());
            Assert.Equal(JsonValueKind.Null, scan.GetProperty("attendee").ValueKind);
        }
        var logs = await ScansAsync(ev.Id);
        Assert.Equal(7, logs.Count);
        Assert.All(logs, l => Assert.Equal(CheckInResult.Invalid, l.Result));
        Assert.All(logs, l => Assert.Null(l.AttendeeId));
        Assert.Equal(2, logs.Count(l => l.Method == CheckInMethod.Manual)); // the two participant-ID shapes

        // Stale ticket version: reverting the payment re-issues the QR, so the first one stops working.
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{ev.Id}/registrations/{guest.RegistrationId}/revert-payment", new { reason = "Wrong row" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{ev.Id}/registrations/mark-paid", new { ids = new[] { guest.RegistrationId }, method = "Cash" })).StatusCode);
        var stale = await ScanOkAsync(board, ev.Id, guest.Token);
        Assert.Equal("Invalid", stale.Result());
        var staleLog = (await ScansAsync(ev.Id)).Last();
        Assert.Equal(guest.AttendeeId, staleLog.AttendeeId);
        Assert.Null(await factory.DbAsync(db => db.Attendees.Where(a => a.Id == guest.AttendeeId).Select(a => a.CheckedInAt).SingleAsync()));

        var fresh = (await HoldersAsync(factory, guest.RegistrationId))[0];
        Assert.NotEqual(guest.Token, fresh.Token);
        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, fresh.Token)).Result());
        Assert.Equal(0, await factory.DbAsync(db => db.CheckInScans.CountAsync(s => s.EventId == other.Id)));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("lower")]
    [InlineData("nodash")]
    [InlineData("  {0}  ")]
    public async Task Typed_participant_id_only_looks_up_and_never_checks_in(string shape)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var payload = shape switch
        {
            "lower" => guest.ParticipantId.ToLowerInvariant(),
            "nodash" => guest.ParticipantId.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant(),
            _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, guest.ParticipantId),
        };
        var scan = await ScanOkAsync(board, ev.Id, payload);
        Assert.Equal("Lookup", scan.Result());
        Assert.Equal(guest.AttendeeId, scan.GetProperty("attendeeId").GetGuid());
        Assert.Equal("Paid", scan.GetProperty("status").GetString());
        Assert.Equal(guest.ParticipantId, scan.GetProperty("attendee").GetProperty("participantId").GetString());
        Assert.Equal(JsonValueKind.Null, scan.GetProperty("checkedInAt").ValueKind);
        var log = Assert.Single(await ScansAsync(ev.Id));
        Assert.Equal(CheckInMethod.Manual, log.Method);
        Assert.Equal(CheckInResult.Lookup, log.Result);
        Assert.Equal(guest.AttendeeId, log.AttendeeId);

        // Nothing changed: checked_in_at is still NULL and the registration is still Paid.
        Assert.Null(await factory.DbAsync(db => db.Attendees.Where(a => a.Id == guest.AttendeeId).Select(a => a.CheckedInAt).SingleAsync()));
        Assert.Equal(RegistrationStatus.Paid,
            await factory.DbAsync(db => db.Registrations.Where(r => r.Id == guest.RegistrationId).Select(r => r.Status).SingleAsync()));
        Assert.False(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "attendee.check_in" && a.EntityId == guest.AttendeeId.ToString())));

        // The desk then checks them in explicitly; a later lookup shows when and where.
        Assert.Equal("Welcome", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/check-in/manual",
            new { attendeeId = guest.AttendeeId, station = "Desk 4" }))).Result());
        var again = await ScanOkAsync(board, ev.Id, payload);
        Assert.Equal("Lookup", again.Result());
        Assert.Equal("CheckedIn", again.GetProperty("status").GetString());
        Assert.Equal("Desk 4", again.GetProperty("checkedInStation").GetString());
    }

    [Fact]
    public async Task Manual_check_in_by_attendee_id_uses_the_same_rules()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var other = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var outsider = await PaidAsync(factory, board, other);
        var apprentice = await factory.SignedInAsync(PersonLevel.Apprentice);
        var url = $"/api/events/{ev.Id}/check-in/manual";

        var first = await ApiClient.JsonAsync(await apprentice.PostAsync(url, new { attendeeId = guest.AttendeeId, station = "Help desk" }));
        Assert.Equal("Welcome", first.Result());
        var again = await ApiClient.JsonAsync(await apprentice.PostAsync(url, new { attendeeId = guest.AttendeeId, station = "Desk 3" }));
        Assert.Equal("AlreadyCheckedIn", again.Result());
        Assert.Equal("Help desk", again.GetProperty("checkedInStation").GetString());
        Assert.Equal("Invalid", (await ApiClient.JsonAsync(await apprentice.PostAsync(url, new { attendeeId = outsider.AttendeeId, station = "Desk 3" }))).Result());
        Assert.Equal("Invalid", (await ApiClient.JsonAsync(await apprentice.PostAsync(url, new { attendeeId = Guid.NewGuid(), station = "Desk 3" }))).Result());

        var logs = await ScansAsync(ev.Id);
        Assert.Equal(4, logs.Count);
        Assert.All(logs, l => Assert.Equal(CheckInMethod.Manual, l.Method));
        Assert.Null(await factory.DbAsync(db => db.Attendees.Where(a => a.Id == outsider.AttendeeId).Select(a => a.CheckedInAt).SingleAsync()));
    }

    [Fact]
    public async Task Group_registration_is_checked_in_once_anyone_is_in_and_each_attendee_keeps_their_flag()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board, ticket: Ticket("Team of 3", 3000m, 10, groupSize: 3));
        var team = await RegisterAsync(factory, board, ev.Id, ev.TicketId, people:
            [Person(first: "Bea", last: "Garcia"), Member(first: "Dan", last: "Lim"), Member(first: "Eli", last: "Ramos")]);

        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, team[1].Token)).Result());
        Assert.Equal(RegistrationStatus.CheckedIn,
            await factory.DbAsync(db => db.Registrations.Where(r => r.Id == team[0].RegistrationId).Select(r => r.Status).SingleAsync()));
        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, team[0].Token)).Result());
        Assert.Equal("AlreadyCheckedIn", (await ScanOkAsync(board, ev.Id, team[1].Token)).Result());

        var hits = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/search?q=Ramos"));
        Assert.Equal("Paid", Assert.Single(hits.EnumerateArray()).GetProperty("status").GetString());
        var stats = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/stats"));
        Assert.Equal(2, stats.GetProperty("checkedIn").GetInt32());
        Assert.Equal(3, stats.GetProperty("paid").GetInt32());
    }

    [Fact]
    public async Task Is_member_matches_an_active_person_by_email_case_insensitively()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var guest = await PaidAsync(factory, board, ev, email: apprentice.Email.ToUpperInvariant());
        var scan = await ScanOkAsync(board, ev.Id, guest.Token);
        Assert.True(scan.GetProperty("attendee").GetProperty("isMember").GetBoolean());
    }

    // ---------- Concurrency and the append-only log ----------

    [Fact]
    public async Task Simultaneous_scans_of_one_ticket_give_exactly_one_welcome()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev);
        var desks = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => factory.SignedInAsync(PersonLevel.Goon)));

        var results = await Task.WhenAll(desks.Select((d, i) => ScanOkAsync(d, ev.Id, guest.Token, $"Desk {i}")));
        Assert.Single(results, r => r.Result() == "Welcome");
        Assert.Equal(5, results.Count(r => r.Result() == "AlreadyCheckedIn"));
        var logs = await ScansAsync(ev.Id);
        Assert.Equal(6, logs.Count);
        Assert.Single(logs, l => l.Result == CheckInResult.Welcome);
        Assert.Equal(1, await factory.DbAsync(db => db.AuditEntries.CountAsync(a => a.Action == "attendee.check_in" && a.EntityId == guest.AttendeeId.ToString())));
    }

    [Fact]
    public async Task Simultaneous_scans_of_two_group_members_both_welcome()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board, ticket: Ticket("Pair", 2000m, 10, groupSize: 2));
        var pair = await RegisterAsync(factory, board, ev.Id, ev.TicketId, people: [Person(), Member()]);
        var a = await factory.SignedInAsync(PersonLevel.Goon);
        var b = await factory.SignedInAsync(PersonLevel.Goon);
        var results = await Task.WhenAll(ScanOkAsync(a, ev.Id, pair[0].Token, "A"), ScanOkAsync(b, ev.Id, pair[1].Token, "B"));
        Assert.All(results, r => Assert.Equal("Welcome", r.Result()));
        Assert.Equal(RegistrationStatus.CheckedIn,
            await factory.DbAsync(db => db.Registrations.Where(r => r.Id == pair[0].RegistrationId).Select(r => r.Status).SingleAsync()));
    }

    [Theory]
    [InlineData("UPDATE check_in_scans SET station = 'tampered'")]
    [InlineData("DELETE FROM check_in_scans")]
    [InlineData("TRUNCATE check_in_scans")]
    public async Task Scan_log_is_append_only_in_the_database(string sql)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        await ScanOkAsync(board, ev.Id, "not a ticket");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => factory.DbAsync(db => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Contains("append-only", ex.Message, StringComparison.Ordinal);
        Assert.Single(await ScansAsync(ev.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            var row = await db.CheckInScans.FirstAsync(s => s.EventId == ev.Id);
            db.CheckInScans.Remove(row);
            return await db.SaveChangesAsync();
        }));
    }

    // ---------- Stats, recent, search ----------

    [Fact]
    public async Task Stats_count_checked_in_paid_this_station_and_stations_online()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var a = await PaidAsync(factory, board, ev);
        var b = await PaidAsync(factory, board, ev);
        await PaidAsync(factory, board, ev);
        await RegisterAsync(factory, board, ev.Id, ev.TicketId, paid: false);
        await ScanOkAsync(board, ev.Id, a.Token, "Desk 1");
        await ScanOkAsync(board, ev.Id, b.Token, "Desk 2");
        await ScanOkAsync(board, ev.Id, "garbage", "Desk 3");
        // A scan from long ago doesn't count as online.
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"ALTER TABLE check_in_scans DISABLE TRIGGER check_in_scans_append_only; UPDATE check_in_scans SET at = at - interval '10 minutes' WHERE event_id = {ev.Id} AND station = 'Desk 3'; ALTER TABLE check_in_scans ENABLE TRIGGER check_in_scans_append_only;"));

        var stats = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/stats?station=Desk%201"));
        Assert.Equal(2, stats.GetProperty("checkedIn").GetInt32());
        Assert.Equal(3, stats.GetProperty("paid").GetInt32());
        Assert.Equal(1, stats.GetProperty("thisStation").GetInt32());
        Assert.Equal(2, stats.GetProperty("stationsOnline").GetInt32());

        var noStation = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/stats"));
        Assert.Equal(0, noStation.GetProperty("thisStation").GetInt32());
    }

    [Fact]
    public async Task Recent_is_newest_first_limited_to_20_and_filters_by_station()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev, first: "Ana", last: "Reyes");
        for (var i = 0; i < 21; i++) await ScanOkAsync(board, ev.Id, "junk " + i, "Desk 9");
        await ScanOkAsync(board, ev.Id, guest.Token, "Desk 1");

        var recent = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/recent"))).EnumerateArray().ToList();
        Assert.Equal(20, recent.Count);
        Assert.Equal("Ana Reyes", recent[0].GetProperty("name").GetString());
        Assert.Equal(guest.ParticipantId, recent[0].GetProperty("participantId").GetString());
        Assert.Equal("Welcome", recent[0].GetProperty("result").GetString());
        Assert.Equal("Desk 1", recent[0].GetProperty("station").GetString());
        Assert.Equal(JsonValueKind.Null, recent[1].GetProperty("name").ValueKind);
        Assert.Equal("Invalid", recent[1].GetProperty("result").GetString());
        var times = recent.Select(r => r.GetProperty("at").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        var desk1 = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/recent?station=Desk%201"))).EnumerateArray().ToList();
        Assert.Equal("Ana Reyes", Assert.Single(desk1).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Search_finds_by_name_or_participant_id_with_limited_fields_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await LiveAsync(board);
        var other = await LiveAsync(board);
        var tag = LetterTag("Zq");
        var maria = await PaidAsync(factory, board, ev, first: "Maria", last: tag);
        var juan = (await RegisterAsync(factory, board, ev.Id, ev.TicketId, paid: false, people: [Person(first: "Juan", last: tag)]))[0];
        await PaidAsync(factory, board, other, first: "Maria", last: tag); // other event: never listed
        await ScanOkAsync(board, ev.Id, maria.Token);
        var goon = await factory.SignedInAsync(PersonLevel.Goon);

        var byName = (await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{ev.Id}/check-in/search?q={tag.ToLowerInvariant()}"))).EnumerateArray().ToList();
        Assert.Equal(2, byName.Count);
        foreach (var hit in byName)
            Assert.Equal(["attendeeId", "name", "participantId", "status", "ticketName"], hit.EnumerateObject().Select(p => p.Name).Order());
        var json = string.Join(" ", byName.Select(h => h.GetRawText()));
        Assert.DoesNotContain(maria.Email, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+639", json, StringComparison.Ordinal);
        Assert.Equal("CheckedIn", byName.Single(h => h.GetProperty("attendeeId").GetGuid() == maria.AttendeeId).GetProperty("status").GetString());
        Assert.Equal("PendingPayment", byName.Single(h => h.GetProperty("attendeeId").GetGuid() == juan.AttendeeId).GetProperty("status").GetString());

        var full = await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{ev.Id}/check-in/search?q=Maria%20{tag}"));
        Assert.Equal(maria.AttendeeId, Assert.Single(full.EnumerateArray()).GetProperty("attendeeId").GetGuid());
        var byId = await ApiClient.JsonAsync(await goon.GetAsync(
            $"/api/events/{ev.Id}/check-in/search?q={juan.ParticipantId.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant()}"));
        Assert.Equal(juan.AttendeeId, Assert.Single(byId.EnumerateArray()).GetProperty("attendeeId").GetGuid());
        // IDs match by a prefix of at least 6 characters (dashes ignored), never as a substring.
        async Task<List<Guid>> HitsAsync(string q) =>
            (await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{ev.Id}/check-in/search?q={Uri.EscapeDataString(q)}")))
            .EnumerateArray().Select(h => h.GetProperty("attendeeId").GetGuid()).ToList();
        var compact = juan.ParticipantId.Replace("-", "", StringComparison.Ordinal);
        Assert.Contains(juan.AttendeeId, await HitsAsync(compact[..6].ToLowerInvariant()));      // "htn7k3"
        Assert.Contains(juan.AttendeeId, await HitsAsync(juan.ParticipantId[..9]));               // "HTN-7K3Q-"
        Assert.Empty(await HitsAsync(compact[..5]));                                              // too short
        Assert.Empty(await HitsAsync(juan.ParticipantId[4..]));                                   // substring
        Assert.Empty(await HitsAsync(juan.ParticipantId[9..]));                                   // suffix

        // Names need 3 characters; wildcards are escaped; email searches find nothing.
        Assert.Empty(await HitsAsync("%%%"));
        Assert.Empty(await HitsAsync("___"));
        Assert.Empty(await HitsAsync("Ma"));
        Assert.Equal(2, (await HitsAsync(tag[..5])).Count);
        Assert.Empty((await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{ev.Id}/check-in/search?q=M"))).EnumerateArray());
        Assert.Empty((await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{ev.Id}/check-in/search?q={Uri.EscapeDataString(maria.Email)}"))).EnumerateArray());
    }

    [Fact]
    public async Task Search_is_capped_at_20()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        // Groups over 20 are hidden tickets (manual registration only).
        var ev = await LiveAsync(board);
        var big = (await AddTicketAsync(board, ev.Id, Ticket("Big team", 1000m, 10, groupSize: 25, visibility: "Hidden"))).Id();
        var tag = LetterTag("Cap");
        await RegisterAsync(factory, board, ev.Id, big, paid: false,
            people: [Person(last: tag), .. Enumerable.Range(0, 24).Select(i => Member(first: "P" + (char)('a' + i), last: tag))]);
        var hits = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/check-in/search?q={tag}"));
        Assert.Equal(20, hits.GetArrayLength());
    }
}
