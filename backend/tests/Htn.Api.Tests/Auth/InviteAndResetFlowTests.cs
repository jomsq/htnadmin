using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Outbox;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Auth;

public sealed class InviteAndResetFlowTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private const string NewPassword = "brand-new-password-42";

    private async Task<string> InviteGoonAsync(string email)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostAsync("/api/people", new { firstName = "Mika", lastName = "Soriano", email, level = "Goon", position = "Head of Logistics" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        await factory.ProcessOutboxAsync();
        return factory.Emails.LastToken(email, "invite");
    }

    [Fact]
    public async Task Create_then_accept_invite_then_login()
    {
        var email = HtnApiFactory.UniqueEmail("mika");
        var token = await InviteGoonAsync(email);

        var mail = factory.Emails.To(email).Single();
        Assert.Equal(EmailCategories.Invite, mail.Category);
        Assert.Contains("https://admin.test/invite/", mail.Text, StringComparison.Ordinal);
        Assert.Contains("https://admin.test/invite/", mail.Html, StringComparison.Ordinal);
        Assert.Contains("#B71C1C", mail.Html, StringComparison.Ordinal);
        Assert.Contains("HackTheNorth.PH", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Head of Logistics", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, mail.Subject, StringComparison.Ordinal);

        // After sending, the outbox row is Sent and the raw token is gone from the database.
        var outbox = (await factory.DbAsync(db => db.Outbox.AsNoTracking().ToListAsync())).Single(o => o.PayloadJson.Contains(email, StringComparison.Ordinal));
        Assert.Equal(OutboxStatus.Sent, outbox.Status);
        Assert.Null(outbox.Secret);
        Assert.NotNull(outbox.ExternalId);

        var anon = factory.NewClient();
        var accept = await anon.PostAsync("/api/auth/invite/accept", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.False((await ApiClient.JsonAsync(accept)).GetProperty("requiresTwoFactorSetup").GetBoolean()); // Goon

        var login = await anon.LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("Goon", (await ApiClient.JsonAsync(login)).GetProperty("level").GetString());

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == email));
        Assert.True(user.EmailConfirmed);
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "user.invite_accepted" && a.EntityId == user.Id.ToString())));

        var board = await factory.SignedInAsync(PersonLevel.Board);
        var list = await ApiClient.JsonAsync(await board.GetAsync($"/api/people?search={Uri.EscapeDataString(email)}"));
        Assert.Equal("Active", list.GetProperty("items")[0].GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async Task Invite_token_is_single_use()
    {
        var email = HtnApiFactory.UniqueEmail("reuse");
        var token = await InviteGoonAsync(email);
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync("/api/auth/invite/accept", new { token, password = NewPassword })).StatusCode);

        var again = await anon.PostAsync("/api/auth/invite/accept", new { token, password = "another-password-123" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("invite_invalid", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Short_password_is_rejected_and_token_stays_usable()
    {
        var email = HtnApiFactory.UniqueEmail("short");
        var token = await InviteGoonAsync(email);
        var anon = factory.NewClient();

        var res = await anon.PostAsync("/api/auth/invite/accept", new { token, password = "short-11chr" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).GetProperty("errors").TryGetProperty("password", out _));

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync("/api/auth/invite/accept", new { token, password = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task Unknown_invite_token_is_rejected()
    {
        var res = await factory.NewClient().PostAsync("/api/auth/invite/accept", new { token = "not-a-real-token", password = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Expired_invite_token_is_rejected()
    {
        var email = HtnApiFactory.UniqueEmail("expired");
        var token = await InviteGoonAsync(email);
        await factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE account_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = (SELECT id FROM users WHERE email = {email})"));
        var res = await factory.NewClient().PostAsync("/api/auth/invite/accept", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_is_202_and_queues_nothing()
    {
        var before = await factory.DbAsync(db => db.Outbox.CountAsync());
        var res = await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email = HtnApiFactory.UniqueEmail("ghost") });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(before, await factory.DbAsync(db => db.Outbox.CountAsync()));
    }

    [Fact]
    public async Task Forgot_then_reset_password_then_login()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var anon = factory.NewClient();

        var forgot = await anon.PostAsync("/api/auth/password/forgot", new { email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        await factory.ProcessOutboxAsync();
        var mail = factory.Emails.To(user.Email).Last();
        Assert.Equal(EmailCategories.PasswordReset, mail.Category);
        Assert.Contains("30 minutes", mail.Text, StringComparison.Ordinal);
        var token = factory.Emails.LastToken(user.Email, "reset");

        var expiry = await factory.DbAsync(db => db.AccountTokens.Where(t => t.UserId == user.UserId).Select(t => t.ExpiresAt - t.CreatedAt).SingleAsync());
        Assert.Equal(TimeSpan.FromMinutes(30), expiry);

        var reset = await anon.PostAsync("/api/auth/password/reset", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.LoginAsync(user.Email, HtnApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.LoginAsync(user.Email, NewPassword)).StatusCode);

        var reuse = await factory.NewClient().PostAsync("/api/auth/password/reset", new { token, password = "yet-another-password" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Reset_with_mismatched_email_is_rejected()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        var anon = factory.NewClient();
        await anon.PostAsync("/api/auth/password/forgot", new { email = user.Email });
        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(user.Email, "reset");

        var res = await anon.PostAsync("/api/auth/password/reset", new { token, email = "someone@else.ph", password = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
