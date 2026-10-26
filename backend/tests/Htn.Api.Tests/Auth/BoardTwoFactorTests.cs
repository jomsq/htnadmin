using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;

namespace Htn.Api.Tests.Auth;

public sealed class BoardTwoFactorTests(Board2faApiFactory factory) : IClassFixture<Board2faApiFactory>
{
    [Fact]
    public async Task Board_without_2fa_is_signed_in_but_gated()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        var login = await client.LoginAsync(board.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await ApiClient.JsonAsync(login)).GetProperty("twoFactorRequired").GetBoolean());

        var people = await client.GetAsync("/api/people");
        Assert.Equal(HttpStatusCode.Forbidden, people.StatusCode);
        Assert.Equal("2fa_required", (await ApiClient.JsonAsync(people)).GetProperty("code").GetString());

        var create = await client.PostAsync("/api/people", new { firstName = "A", lastName = "B", email = HtnApiFactory.UniqueEmail(), level = "Member" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var prefs = await client.PutAsync("/api/me/preferences", new { mode = "dark", textSize = "default", density = "compact" });
        Assert.Equal(HttpStatusCode.Forbidden, prefs.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Board_sets_up_2fa_and_is_ungated_then_logs_in_with_code()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        await client.LoginOkAsync(board.Email, HtnApiFactory.Password);

        var setup = await client.PostAsync("/api/auth/2fa/setup");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await ApiClient.JsonAsync(setup);
        var key = body.GetProperty("sharedKey").GetString()!;
        var uri = body.GetProperty("authenticatorUri").GetString()!;
        Assert.StartsWith("otpauth://totp/HackTheNorth.PH:", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=HackTheNorth.PH", uri, StringComparison.Ordinal);

        var bad = await client.PostAsync("/api/auth/2fa/enable", new { code = "000000" == Totp.Code(key) ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var enable = await client.PostAsync("/api/auth/2fa/enable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/people")).StatusCode);
        var me = await ApiClient.JsonAsync(await client.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.False(me.GetProperty("twoFactorRequired").GetBoolean());

        // Next sign-in needs the code.
        var fresh = factory.NewClient();
        var challenge = await fresh.LoginAsync(board.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        Assert.True((await ApiClient.JsonAsync(challenge)).GetProperty("requires2fa").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/auth/me")).StatusCode);

        var wrong = await fresh.PostAsync("/api/auth/login/2fa", new { code = "12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var ok = await fresh.PostAsync("/api/auth/login/2fa", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Board", (await ApiClient.JsonAsync(ok)).GetProperty("level").GetString());
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/people")).StatusCode);
    }

    [Fact]
    public async Task Login_2fa_without_password_step_is_401()
    {
        var res = await factory.NewClient().PostAsync("/api/auth/login/2fa", new { code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Goons_are_not_gated()
    {
        var client = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/people")).StatusCode);
    }

    [Fact]
    public async Task Two_factor_setup_requires_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().PostAsync("/api/auth/2fa/setup")).StatusCode);
    }
}
