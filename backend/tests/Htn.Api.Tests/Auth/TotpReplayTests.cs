using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Auth;

/// <summary>An authenticator code (time step) is accepted at most once per user, at login and at 2FA enable.</summary>
public sealed class TotpReplayTests(Board2faApiFactory factory) : IClassFixture<Board2faApiFactory>
{
    private static async Task<HttpResponseMessage> TwoFactorLoginAsync(ApiClient client, string email, string code)
    {
        var challenge = await client.LoginAsync(email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        return await client.PostAsync("/api/auth/login/2fa", new { code });
    }

    [Fact]
    public async Task Login_code_cannot_be_replayed()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board, twoFactor: true);
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Code(board.AuthenticatorKey!, now);

        Assert.Equal(HttpStatusCode.OK, (await TwoFactorLoginAsync(factory.NewClient(), board.Email, code)).StatusCode);

        // Same code from another client (e.g. shoulder-surfed or phished) is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await TwoFactorLoginAsync(factory.NewClient(), board.Email, code)).StatusCode);

        // An older step is refused too; the next step is accepted.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await TwoFactorLoginAsync(factory.NewClient(), board.Email, Totp.Code(board.AuthenticatorKey!, now.AddSeconds(-30)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await TwoFactorLoginAsync(factory.NewClient(), board.Email, Totp.Code(board.AuthenticatorKey!, now.AddSeconds(30)))).StatusCode);

        var step = await factory.DbAsync(db => db.Users.Where(u => u.Id == board.UserId).Select(u => u.LastTotpTimeStep).SingleAsync());
        Assert.Equal(ReplayProtectedAuthenticatorTokenProvider.CurrentStep(now.AddSeconds(30)), step);
    }

    [Fact]
    public async Task Same_code_submitted_concurrently_signs_in_only_once()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board, twoFactor: true);
        var a = factory.NewClient();
        var b = factory.NewClient();
        Assert.Equal(HttpStatusCode.Accepted, (await a.LoginAsync(board.Email, HtnApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await b.LoginAsync(board.Email, HtnApiFactory.Password)).StatusCode);

        var code = Totp.Code(board.AuthenticatorKey!);
        var results = await Task.WhenAll(
            a.PostAsync("/api/auth/login/2fa", new { code }),
            b.PostAsync("/api/auth/login/2fa", new { code }));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Code_used_to_enable_2fa_cannot_be_replayed_at_login()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        await client.LoginOkAsync(board.Email, HtnApiFactory.Password);
        var setup = await ApiClient.JsonAsync(await client.PostAsync("/api/auth/2fa/setup"));
        var key = setup.GetProperty("sharedKey").GetString()!;

        var now = DateTimeOffset.UtcNow;
        var code = Totp.Code(key, now);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/2fa/enable", new { code })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await TwoFactorLoginAsync(factory.NewClient(), board.Email, code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TwoFactorLoginAsync(factory.NewClient(), board.Email, Totp.Code(key, now.AddSeconds(30)))).StatusCode);
    }

    [Fact]
    public async Task Enable_rejects_a_code_older_than_the_last_accepted_one()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        await client.LoginOkAsync(board.Email, HtnApiFactory.Password);
        var setup = await ApiClient.JsonAsync(await client.PostAsync("/api/auth/2fa/setup"));
        var key = setup.GetProperty("sharedKey").GetString()!;

        // Simulate a step already spent (as if a newer code had been accepted).
        var spent = ReplayProtectedAuthenticatorTokenProvider.CurrentStep(DateTimeOffset.UtcNow) + 1;
        await factory.DbAsync(db => db.Users.Where(u => u.Id == board.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpTimeStep, spent)));

        var replay = await client.PostAsync("/api/auth/2fa/enable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.False(await factory.DbAsync(db => db.Users.Where(u => u.Id == board.UserId).Select(u => u.TwoFactorEnabled).SingleAsync()));
    }

    [Fact]
    public async Task Replay_guard_survives_identity_updates()
    {
        // Identity's UpdateAsync marks every column modified; the stored step must not be overwritten by a stale value.
        var board = await factory.CreateUserAsync(PersonLevel.Board, twoFactor: true);
        var code = Totp.Code(board.AuthenticatorKey!);
        Assert.Equal(HttpStatusCode.OK, (await TwoFactorLoginAsync(factory.NewClient(), board.Email, code)).StatusCode);
        Assert.NotNull(await factory.DbAsync(db => db.Users.Where(u => u.Id == board.UserId).Select(u => u.LastTotpTimeStep).SingleAsync()));
    }
}
