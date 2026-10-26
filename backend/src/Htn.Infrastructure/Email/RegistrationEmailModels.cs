using System.Globalization;

namespace Htn.Infrastructure.Email;

/// <summary>Outbox payloads for registration emails. They hold IDs only; the content is loaded when the email is sent.</summary>
public sealed record BillingEmailPayload(Guid RegistrationId);

/// <summary>One attendee's ticket to <see cref="To"/> (the attendee, or the group buyer's copy) at <see cref="TicketVersion"/>.</summary>
public sealed record TicketEmailPayload(Guid AttendeeId, string To, int TicketVersion);

public sealed record RegistrationCancelledEmailPayload(Guid RegistrationId);

/// <summary>A sample billing or ticket email for the per-event email settings page, sent to the signed-in admin.</summary>
public sealed record EventTestEmailPayload(Guid EventId, string Template, string To, string FirstName);

public static class RegistrationEmailTypes
{
    public const string Billing = "email.billing";
    public const string Ticket = "email.ticket";
    public const string Cancelled = "email.registration_cancelled";
    public const string Test = "email.event_test";

    public const string TestBilling = "billing";
    public const string TestTicket = "ticket";
}

/// <summary>A "where to pay" line on the billing email.</summary>
public sealed record PayToLine(string Heading, string AccountName, string? AccountNumber);

/// <summary>Formatting shared by the registration emails: pesos, and times in Asia/Manila (UTC+8, no DST).</summary>
public static class EmailFormat
{
    public static readonly TimeSpan Manila = TimeSpan.FromHours(8);

    public static string Peso(decimal amount) =>
        "₱" + amount.ToString(decimal.Truncate(amount) == amount ? "#,##0" : "#,##0.00", CultureInfo.InvariantCulture);

    public static string When(DateTimeOffset at) =>
        at.ToOffset(Manila).ToString("MMM d, yyyy · h:mm tt", CultureInfo.InvariantCulture);
}
