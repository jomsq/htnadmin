using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Events;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.Registrations;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Api.Tests.CheckIn;

/// <summary>One attendee of a manual registration, with the signed QR payload for their current ticket version.</summary>
internal sealed record TicketHolder(Guid EventId, Guid RegistrationId, Guid AttendeeId, string ParticipantId, string Email, string Name, string Token);

internal static class CheckInTestData
{
    public static string ScanUrl(Guid eventId) => $"/api/events/{eventId}/check-in/scan";

    public static object Scan(string payload, string station = "Main entrance") => new { payload, station };

    /// <summary>
    /// A manual registration on <paramref name="ev"/> (group of <paramref name="people"/> when the ticket allows it), marked
    /// paid (Cash) unless <paramref name="paid"/> is false. Returns every attendee in form order.
    /// </summary>
    public static async Task<List<TicketHolder>> RegisterAsync(
        HtnApiFactory factory, ApiClient board, Guid eventId, Guid ticketTypeId, bool paid = true, object[]? people = null)
    {
        people ??= [RegistrationTestData.Person(HtnApiFactory.UniqueEmail("guest"), first: "Maria", last: "Santos")];
        var res = await board.PostAsync($"/api/events/{eventId}/registrations", paid
            ? new { ticketTypeId, attendees = people, consent = true, markPaid = new { method = "Cash" } }
            : (object)new { ticketTypeId, attendees = people, consent = true });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Manual registration failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var regId = (await ApiClient.JsonAsync(res)).Id();
        return await HoldersAsync(factory, regId);
    }

    public static async Task<TicketHolder> PaidAsync(HtnApiFactory factory, ApiClient board, OnSaleEvent ev, string? email = null,
        string first = "Maria", string last = "Santos") =>
        (await RegisterAsync(factory, board, ev.Id, ev.TicketId, people:
            [RegistrationTestData.Person(email ?? HtnApiFactory.UniqueEmail("guest"), first: first, last: last)]))[0];

    /// <summary>The attendees of a registration with a token for each one's current ticket version.</summary>
    public static async Task<List<TicketHolder>> HoldersAsync(HtnApiFactory factory, Guid registrationId)
    {
        var tokens = factory.Services.GetRequiredService<ITicketToken>();
        var rows = await factory.DbAsync(db => db.Attendees.AsNoTracking().Where(a => a.RegistrationId == registrationId)
            .OrderBy(a => a.SortOrder).ToListAsync());
        return rows.Select(a => new TicketHolder(a.EventId, a.RegistrationId, a.Id, a.ParticipantId, a.Email, a.FullName,
            tokens.Issue(a.EventId, a.Id, a.TicketVersion))).ToList();
    }

    public static async Task<JsonElement> ScanOkAsync(ApiClient client, Guid eventId, string payload, string station = "Main entrance")
    {
        var res = await client.PostAsync(ScanUrl(eventId), Scan(payload, station));
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Scan failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static string Result(this JsonElement scan) => scan.GetProperty("result").GetString()!;

    /// <summary>
    /// Straight to the database: a paid attendee with <paramref name="email"/> checked in at a fresh event, so the Member
    /// rule is satisfied for that email. No scan row (that needs a staff user); the rule reads attendee check-ins.
    /// </summary>
    public static Task<Guid> SeedCheckInAsync(this HtnApiFactory factory, string email, string? eventName = null) =>
        factory.DbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var ev = Event.Create(eventName ?? "Seeded " + Guid.NewGuid().ToString("N")[..6], "seed-" + Guid.NewGuid().ToString("N")[..10],
                now.AddDays(-1), now.AddHours(6), "Laoag City", 100, null, now, null);
            var ticket = TicketType.Create(ev.Id, "Regular", 0m, 10, 1, null, null, TicketVisibility.Public, 1, now);
            ev.Publish(hasActiveTicket: true, now, null);
            db.Events.Add(ev);
            db.TicketTypes.Add(ticket);
            var person = new AttendeeDetails("Seed", null, "Attendee", null, email, null, null, null, null, null);
            var reg = Registration.Create(ev.Id, ticket, [person], [new PublicIdGenerator().NewParticipantId()], null,
                RegistrationSource.Public, null, RegistrationForm.ConsentVersion, null, null, now);
            db.Registrations.Add(reg);
            db.Payments.Add(reg.MarkPaid(PaymentMethod.Comp, null, null, null, null, now));
            reg.CheckIn(reg.Attendees[0].Id, "Seed desk", Guid.NewGuid(), now);
            await db.SaveChangesAsync();
            return ev.Id;
        });
}
