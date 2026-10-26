using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

public sealed class EmailSettingsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Settings_fall_back_to_org_defaults_and_validate_contrast()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var url = $"/api/events/{ev.Id}/email-settings";

        var current = await ApiClient.JsonAsync(await board.GetAsync(url));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, current.GetProperty("accentColor").ValueKind);
        Assert.Empty(current.GetProperty("paymentAccountIds").EnumerateArray());
        Assert.Equal("#B71C1C", current.GetProperty("defaults").GetProperty("accentColor").GetString());
        Assert.Equal(48, current.GetProperty("defaults").GetProperty("paymentDeadlineHours").GetInt32());

        foreach (var bad in new[] { "#FFEB3B", "#9CA3AF", "#FFFFFF" })
        {
            var res = await board.PutAsync(url, new { accentColor = bad, paymentAccountIds = Array.Empty<Guid>() });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await ApiClient.JsonAsync(res)).HasError("accentColor"), bad);
        }
        foreach (var bad in new[] { "red", "#B71C1", "#B71C1C\n" })
            Assert.True((await ApiClient.JsonAsync(await board.PutAsync(url, new { accentColor = bad }))).HasError("accentColor"), bad);
        Assert.True((await ApiClient.JsonAsync(await board.PutAsync(url, new { paymentDeadlineHours = 0 }))).HasError("paymentDeadlineHours"));
        var unknown = await board.PutAsync(url, new { paymentAccountIds = new[] { Guid.NewGuid() } });
        Assert.Equal("unknown_payment_account", (await ApiClient.JsonAsync(unknown)).Code());

        var account = await ApiClient.JsonAsync(await board.PostAsync("/api/settings/payment-accounts",
            new { kind = "Bank", label = "BPI", bankName = "BPI", accountName = "HackTheNorth.PH Inc.", accountNumber = "1234 5678 90" }));
        var ok = await board.PutAsync(url, new { accentColor = "#0f766e", paymentAccountIds = new[] { account.Id() }, paymentDeadlineHours = 24 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var saved = await ApiClient.JsonAsync(ok);
        Assert.Equal("#0F766E", saved.GetProperty("accentColor").GetString());
        Assert.Equal(24, saved.GetProperty("paymentDeadlineHours").GetInt32());
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "event.email_settings_update" && a.EntityId == ev.Id.ToString())));

        // A registration now gets a 24 h deadline, the accent color and only that account.
        var email = HtnApiFactory.UniqueEmail("styled");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        Assert.InRange(created.GetProperty("deadline").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
        await factory.DrainOutboxAsync();
        var mail = factory.Emails.To(email).Single();
        Assert.Contains("#0F766E", mail.Html, StringComparison.Ordinal);
        Assert.Contains("BANK TRANSFER · BPI", mail.Text, StringComparison.Ordinal);
        Assert.Contains("1234 5678 90", mail.Html, StringComparison.Ordinal);
        Assert.Contains("held for 24 hours", mail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_email_goes_to_the_signed_in_admin_with_the_events_settings()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Board);
        var board = factory.NewClient();
        await board.LoginOkAsync(user.Email, HtnApiFactory.Password);
        var ev = await PublishedAsync(board);

        Assert.True((await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{ev.Id}/email-settings/test", new { template = "invite" }))).HasError("template"));
        foreach (var template in new[] { "billing", "ticket" })
        {
            var res = await board.PostAsync($"/api/events/{ev.Id}/email-settings/test", new { template });
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            Assert.Equal(user.Email, (await ApiClient.JsonAsync(res)).GetProperty("to").GetString());
        }
        await factory.DrainOutboxAsync();
        var mails = factory.Emails.To(user.Email);
        Assert.Contains(mails, m => m.Category == EmailCategories.Billing && m.Subject.StartsWith("[Test] Complete your payment", StringComparison.Ordinal));
        var ticket = Assert.Single(mails, m => m.Category == EmailCategories.Ticket);
        Assert.StartsWith("[Test] ", ticket.Subject, StringComparison.Ordinal);
        Assert.Contains(ticket.AllAttachments, a => a.IsInline && a.ContentType == "image/png");
        Assert.Contains(RegistrationEmails.SampleParticipantId, ticket.Html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{Guid.NewGuid()}/email-settings/test", new { template = "billing" })).StatusCode);
    }

    [Fact]
    public async Task Org_logo_is_inlined_in_registration_and_account_emails()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var upload = await board.PostFileAsync("/api/settings/branding/logo?variant=light", Png, "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var ev = await PublishedAsync(board);
        var email = HtnApiFactory.UniqueEmail("logo");
        await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));

        Assert.Equal(HttpStatusCode.Accepted, (await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email = (await factory.CreateUserAsync(PersonLevel.Goon)).Email })).StatusCode);
        await factory.DrainOutboxAsync();

        var billing = factory.Emails.To(email).Single();
        var logo = Assert.Single(billing.AllAttachments, a => a.ContentId == EmailRenderer.LogoContentId);
        Assert.True(logo.IsInline);
        Assert.Equal(Png, logo.Content);
        Assert.Contains("cid:" + EmailRenderer.LogoContentId, billing.Html, StringComparison.Ordinal);
        var reset = factory.Emails.Sent.Last(m => m.Category == EmailCategories.PasswordReset);
        Assert.Contains(reset.AllAttachments, a => a.ContentId == EmailRenderer.LogoContentId);
        Assert.Contains("cid:" + EmailRenderer.LogoContentId, reset.Html, StringComparison.Ordinal);
    }
}
