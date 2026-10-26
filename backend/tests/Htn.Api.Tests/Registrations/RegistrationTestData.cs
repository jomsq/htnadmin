using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Events;
using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Registrations;

internal sealed record OnSaleEvent(Guid Id, string Slug, Guid TicketId);

internal static class RegistrationTestData
{
    public static object Person(string? email = null, string first = "Maria", string last = "Santos", string? mobile = "9171234567",
        string? gender = "Female", string? company = "DICT Region 1") => new
    {
        firstName = first, middleName = (string?)null, lastName = last, gender, email = email ?? HtnApiFactory.UniqueEmail("reg"),
        mobile, occupation = "Engineer", company, shirtSize = "M", hearAbout = "Facebook",
    };

    public static object Member(string? email = null, string first = "Jo", string last = "Reyes") => new
    {
        firstName = first, lastName = last, email = email ?? HtnApiFactory.UniqueEmail("member"),
    };

    public static object Body(Guid ticketTypeId, object[] attendees, string? voucherCode = null, Guid? key = null, bool consent = true,
        string website = "") => new
    {
        idempotencyKey = key ?? Guid.NewGuid(), ticketTypeId, voucherCode, attendees, consent, website,
    };

    /// <summary>An on-sale event with one public ticket (and optional extra settings applied before publishing).</summary>
    public static async Task<OnSaleEvent> PublishedAsync(ApiClient board, int capacity = 400, object? ticket = null)
    {
        var ev = await CreateAsync(board, NewEvent(capacity: capacity));
        var id = ev.Id();
        var t = await AddTicketAsync(board, id, ticket ?? Ticket());
        var res = await board.PostAsync($"/api/events/{id}/publish");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Publish failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return new OnSaleEvent(id, ev.GetProperty("slug").GetString()!, t.Id());
    }

    public static async Task<Guid> AddVoucherAsync(ApiClient board, Guid eventId, object body)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/vouchers", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add voucher failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).Id();
    }

    public static Task<HttpResponseMessage> RegisterAsync(ApiClient client, string slug, object body) =>
        client.PostAsync($"/api/public/events/{slug}/registrations", body);

    public static async Task<JsonElement> RegisterOkAsync(ApiClient client, string slug, object body)
    {
        var res = await RegisterAsync(client, slug, body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Register failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    /// <summary>The registration ID for a participant ID.</summary>
    public static Task<Guid> RegistrationIdAsync(HtnApiFactory factory, string participantId) =>
        factory.DbAsync(db => db.Attendees.AsNoTracking().Where(a => a.ParticipantId == participantId).Select(a => a.RegistrationId).SingleAsync());

    /// <summary>
    /// Moves a pending registration's deadline into the past (and its creation before it, which ck_registrations_deadline
    /// requires). created_at is frozen by the registrations_guard trigger, so the trigger is off for this one statement.
    /// </summary>
    public static Task MakeOverdueAsync(this HtnApiFactory factory, Guid registrationId) =>
        factory.DbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE registrations DISABLE TRIGGER registrations_guard");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE registrations SET created_at = now() - interval '3 days', consent_at = now() - interval '3 days', deadline = now() - interval '5 minutes' WHERE id = {registrationId}");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE registrations ENABLE TRIGGER registrations_guard");
            await tx.CommitAsync();
            return 0;
        });

    public static async Task DrainOutboxAsync(this HtnApiFactory factory)
    {
        while (await factory.ProcessOutboxAsync() > 0)
        {
        }
    }

    /// <summary>The raw proof token from the newest billing email to <paramref name="email"/>.</summary>
    public static string ProofToken(this FakeEmailSender emails, string email)
    {
        var text = emails.To(email).Last(m => m.Category == EmailCategories.Billing).Text;
        var match = System.Text.RegularExpressions.Regex.Match(text, "/pay/([A-Za-z0-9_%-]+)");
        if (!match.Success) throw new InvalidOperationException("No proof link in the billing email.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    /// <summary>A minimal valid PNG (1×1, signature + IHDR + IDAT + IEND).</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static string[] Strings(this JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
