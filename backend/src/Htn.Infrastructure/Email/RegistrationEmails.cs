using System.Text;
using Htn.Domain.Events;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Builds billing, ticket, cancelled and test emails from the current database state when the outbox sends them.
/// Returns null when the email no longer applies (paid before the billing went out, ticket re-issued or revoked), so
/// a stale message is never delivered.
/// </summary>
public sealed class RegistrationEmails(HtnDbContext db, EmailRenderer renderer, ITicketToken tickets, IOptions<AppLinkOptions> links, TimeProvider clock)
{
    public const string QrContentId = "ticket-qr";
    public const string SampleParticipantId = "HTN-XXXX-XXXX";

    public async Task<EmailMessage?> BillingAsync(BillingEmailPayload p, string? rawProofToken)
    {
        var reg = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == p.RegistrationId);
        if (reg is null || reg.Status != RegistrationStatus.PendingPayment || reg.Deadline is not { } deadline) return null;
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == reg.EventId);
        var ticket = await db.TicketTypes.AsNoTracking().SingleAsync(t => t.Id == reg.TicketTypeId);
        var buyer = reg.Buyer;
        var hours = Math.Max(1, (int)Math.Round((deadline - reg.CreatedAt).TotalHours));
        var uploadLink = rawProofToken is null ? null : links.Value.Proof(rawProofToken);
        return await BillingMessageAsync(ev, buyer.Email, buyer.FirstName, reg.Total, buyer.ParticipantId, TicketLine(ticket, reg.Seats),
            deadline, hours, uploadLink, subjectPrefix: "");
    }

    public async Task<EmailMessage?> TicketAsync(TicketEmailPayload p)
    {
        var attendee = await db.Attendees.AsNoTracking().SingleOrDefaultAsync(a => a.Id == p.AttendeeId);
        if (attendee is null || attendee.TicketVersion != p.TicketVersion) return null;
        var reg = await db.Registrations.AsNoTracking().SingleAsync(r => r.Id == attendee.RegistrationId);
        if (reg.Status is not (RegistrationStatus.Paid or RegistrationStatus.CheckedIn)) return null;
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == reg.EventId);
        var ticket = await db.TicketTypes.AsNoTracking().SingleAsync(t => t.Id == reg.TicketTypeId);

        var forAttendee = string.Equals(p.To.Trim(), attendee.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        var greet = forAttendee ? attendee.FirstName : reg.Buyer.FirstName;
        var token = tickets.Issue(ev.Id, attendee.Id, attendee.TicketVersion);
        return await TicketMessageAsync(ev, p.To, greet, attendee.FullName, !forAttendee, ticket.Name, attendee.ParticipantId, attendee.Id,
            TicketQr.Png(token), subjectPrefix: "");
    }

    public async Task<EmailMessage?> CancelledAsync(RegistrationCancelledEmailPayload p)
    {
        var reg = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == p.RegistrationId);
        if (reg is null || reg.Status != RegistrationStatus.Cancelled) return null;
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == reg.EventId);
        var buyer = reg.Buyer;
        var ids = string.Join(", ", reg.Attendees.OrderBy(a => a.SortOrder).Select(a => a.ParticipantId));
        var (color, logo) = await BrandingAsync(ev);
        var html = await renderer.RenderAsync<RegistrationCancelledEmail>(new Dictionary<string, object?>
        {
            [nameof(RegistrationCancelledEmail.FirstName)] = buyer.FirstName,
            [nameof(RegistrationCancelledEmail.EventName)] = ev.Name,
            [nameof(RegistrationCancelledEmail.ParticipantIds)] = ids,
            [nameof(RegistrationCancelledEmail.BrandColor)] = color,
            [nameof(RegistrationCancelledEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {buyer.FirstName},

            Your registration for {ev.Name} ({ids}) was cancelled. Any QR code sent for it no longer works.
            If you think this is a mistake, reply to this email or contact the organizers.

            {Brand.OrgName}
            """;
        return new EmailMessage(buyer.Email, buyer.FullName, $"Your {ev.Name} registration was cancelled", html, text,
            EmailCategories.Registration, logo is null ? null : [logo]);
    }

    /// <summary>A sample with the event's current settings. The ticket sample's QR is not a valid ticket.</summary>
    public async Task<EmailMessage?> TestAsync(EventTestEmailPayload p)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == p.EventId);
        if (ev is null) return null;
        var ticket = await db.TicketTypes.AsNoTracking().Where(t => t.EventId == ev.Id && t.IsActive)
            .OrderBy(t => t.SortOrder).FirstOrDefaultAsync();
        var ticketName = ticket?.Name ?? "Regular";
        if (p.Template == RegistrationEmailTypes.TestTicket)
            return await TicketMessageAsync(ev, p.To, p.FirstName, p.FirstName + " (sample)", false, ticketName, SampleParticipantId, ev.Id,
                TicketQr.Png("HTN-SAMPLE-NOT-A-TICKET"), subjectPrefix: "[Test] ");

        var hours = ev.PaymentDeadlineHours ?? await OrgDeadlineHoursAsync();
        var now = clock.GetUtcNow();
        var upload = ev.ProofMode == ProofMode.Upload ? links.Value.Proof("sample-link") : null;
        return await BillingMessageAsync(ev, p.To, p.FirstName, ticket?.Price ?? 1500m, SampleParticipantId,
            ticket is null ? "Regular ticket × 1" : TicketLine(ticket, ticket.GroupSize), now.AddHours(hours), hours, upload, subjectPrefix: "[Test] ");
    }

    private async Task<EmailMessage> BillingMessageAsync(
        Event ev, string to, string firstName, decimal amount, string participantId, string ticketLine, DateTimeOffset deadline, int hours,
        string? uploadLink, string subjectPrefix)
    {
        var (color, logo) = await BrandingAsync(ev);
        var payTo = await PayToAsync(ev);
        var html = await renderer.RenderAsync<BillingEmail>(new Dictionary<string, object?>
        {
            [nameof(BillingEmail.FirstName)] = firstName,
            [nameof(BillingEmail.EventName)] = ev.Name,
            [nameof(BillingEmail.Amount)] = EmailFormat.Peso(amount),
            [nameof(BillingEmail.ParticipantId)] = participantId,
            [nameof(BillingEmail.TicketLine)] = ticketLine,
            [nameof(BillingEmail.Deadline)] = EmailFormat.When(deadline),
            [nameof(BillingEmail.DeadlineHours)] = hours,
            [nameof(BillingEmail.PayTo)] = payTo,
            [nameof(BillingEmail.UploadLink)] = uploadLink,
            [nameof(BillingEmail.BrandColor)] = color,
            [nameof(BillingEmail.LogoCid)] = logo?.ContentId,
        });

        var text = new StringBuilder();
        text.Append($"Hi {firstName},\n\n");
        text.Append($"Thanks for registering for {ev.Name}. Your seat is held for {hours} hours.\n");
        text.Append($"Send {EmailFormat.Peso(amount)} with reference {participantId} in the message.\n\n");
        foreach (var line in payTo)
            text.Append($"{line.Heading}: {line.AccountName}{(line.AccountNumber is null ? "" : " " + line.AccountNumber)}\n");
        text.Append($"\n{ticketLine}: {EmailFormat.Peso(amount)}\nPay before: {EmailFormat.When(deadline)}\n\n");
        text.Append(uploadLink is null
            ? $"After paying, reply to this email with a screenshot of your payment and your participant ID {participantId}.\n"
            : $"Upload your proof of payment: {uploadLink}\n");
        text.Append($"Your ticket and QR code arrive once our team confirms payment.\n\n{Brand.OrgName}\n");

        return new EmailMessage(to, firstName, $"{subjectPrefix}Complete your payment for {ev.Name}", html, text.ToString(),
            EmailCategories.Billing, logo is null ? null : [logo]);
    }

    private async Task<EmailMessage> TicketMessageAsync(
        Event ev, string to, string greetName, string attendeeName, bool forSomeoneElse, string ticketName, string participantId, Guid uid,
        byte[] qrPng, string subjectPrefix)
    {
        var (color, logo) = await BrandingAsync(ev);
        var icsName = $"{ev.Slug}.ics";
        var html = await renderer.RenderAsync<TicketEmail>(new Dictionary<string, object?>
        {
            [nameof(TicketEmail.FirstName)] = greetName,
            [nameof(TicketEmail.AttendeeName)] = attendeeName,
            [nameof(TicketEmail.ForSomeoneElse)] = forSomeoneElse,
            [nameof(TicketEmail.EventName)] = ev.Name,
            [nameof(TicketEmail.TicketName)] = ticketName,
            [nameof(TicketEmail.ParticipantId)] = participantId,
            [nameof(TicketEmail.When)] = EmailFormat.When(ev.StartsAt),
            [nameof(TicketEmail.Where)] = ev.Venue,
            [nameof(TicketEmail.QrCid)] = QrContentId,
            [nameof(TicketEmail.CalendarFileName)] = icsName,
            [nameof(TicketEmail.BrandColor)] = color,
            [nameof(TicketEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {greetName},

            Payment received. {(forSomeoneElse ? $"This is the ticket for {attendeeName} in your group." : "Show the QR code in this email at the entrance.")}

            Attendee: {attendeeName}
            Ticket: {ticketName} · {participantId}
            When: {EmailFormat.When(ev.StartsAt)}
            Where: {ev.Venue}

            Add it to your calendar with the attached {icsName}. If the QR code doesn't show, give your participant ID at the help desk.

            {Brand.OrgName}
            """;
        var ics = IcsCalendar.Event($"{uid:N}@hackthenorth.ph", ev.StartsAt, ev.EndsAt, ev.Name, ev.Venue,
            $"{ticketName} ticket for {attendeeName} ({participantId}). Show the QR code from your ticket email at the entrance.",
            clock.GetUtcNow());
        List<EmailAttachment> attachments =
        [
            new(QrContentId + ".png", "image/png", qrPng, QrContentId),
            new($"ticket-{participantId}.png", "image/png", qrPng),
            new(icsName, "text/calendar", Encoding.UTF8.GetBytes(ics)),
        ];
        if (logo is not null) attachments.Add(logo);
        return new EmailMessage(to, greetName, $"{subjectPrefix}Your {ev.Name} ticket", html, text, EmailCategories.Ticket, attachments);
    }

    private async Task<(string Color, EmailAttachment? Logo)> BrandingAsync(Event ev)
    {
        var color = ev.EmailAccentColor is { } accent && ColorContrast.IsHex(accent) ? accent : await renderer.BrandColorAsync();
        return (color, await renderer.LogoAsync());
    }

    private async Task<int> OrgDeadlineHoursAsync() =>
        await db.OrgSettings.AsNoTracking().Where(s => s.Id == OrgSettings.SingletonId).Select(s => (int?)s.PaymentDeadlineHours).SingleOrDefaultAsync() ?? 48;

    /// <summary>The event's chosen accounts that are still active; otherwise every active account (default first).</summary>
    private async Task<IReadOnlyList<PayToLine>> PayToAsync(Event ev)
    {
        var chosen = ev.EmailPaymentAccountIds;
        var accounts = chosen.Count == 0
            ? []
            : await db.PaymentAccounts.AsNoTracking().Where(a => a.IsActive && chosen.Contains(a.Id)).ToListAsync();
        if (accounts.Count == 0)
            accounts = await db.PaymentAccounts.AsNoTracking().Where(a => a.IsActive).ToListAsync();
        return accounts
            .OrderBy(a => chosen.Count == 0 ? 0 : chosen.IndexOf(a.Id))
            .ThenByDescending(a => a.IsDefault).ThenBy(a => a.Kind).ThenBy(a => a.Label, StringComparer.Ordinal)
            .Select(a => new PayToLine(
                a.Kind switch
                {
                    PaymentAccountKind.GCash => "GCASH",
                    PaymentAccountKind.Bank => $"BANK TRANSFER · {a.BankName}",
                    _ => "CASH AT THE REGISTRATION DESK",
                },
                a.AccountName, a.AccountNumber))
            .ToList();
    }

    private static string TicketLine(TicketType ticket, int seats) =>
        seats > 1 ? $"{ticket.Name} (group of {seats}) × 1" : $"{ticket.Name} ticket × 1";
}
